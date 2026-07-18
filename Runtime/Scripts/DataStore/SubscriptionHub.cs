/* SubscriptionHub.cs
 *
 * Copyright (c) 2026 University of Minnesota
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IVLab.ABREngine.DataStore
{
    public class SubscribeResult
    {
        public bool Success;
        public string ErrorMessage;
    }

    /// <summary>
    /// Owns the local project store and the set of live downstream
    /// subscribers per project, and fans out ingested data to them.
    ///
    /// Phase 1 scope: single-node only -- a subscribe request for a project
    /// this node doesn't have locally fails immediately. Chaining to an
    /// upstream DataStore (lazy-subscribe-on-cold-miss) is added in a later
    /// phase by extending Subscribe() to consult an UpstreamLink before
    /// giving up; nothing here needs to change shape to accommodate that.
    /// </summary>
    public class SubscriptionHub
    {
        private readonly ILocalProjectStore store;

        // One concurrent set of subscriber channels per project path.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<ISubscriberChannel, byte>> subscribersByProject
            = new ConcurrentDictionary<string, ConcurrentDictionary<ISubscriberChannel, byte>>();

        // Projects that have received a projectinfo/keydata write since the
        // last "update" barrier -- only these get the next update fanned
        // out to their subscribers.
        private readonly ConcurrentDictionary<string, byte> dirtyProjects = new ConcurrentDictionary<string, byte>();

        // The single, node-wide "active" pointer (not per-project) -- see
        // SetActiveAsync/SubscribeActiveAsync. Null/empty project path means
        // nothing is active.
        private string activeProjectPath;
        private string activeStateName;

        // Subscribers of the "active" flavor -- keyed by the caller's own
        // channel (what Unsubscribe(channel) is called with), value is the
        // filtering wrapper actually registered in subscribersByProject.
        private readonly ConcurrentDictionary<ISubscriberChannel, ActiveSubscriberWrapper> activeWrappers
            = new ConcurrentDictionary<ISubscriberChannel, ActiveSubscriberWrapper>();

        // "Only one FE attached at a time" lease. Django's request/response
        // model can't hold one connection open for a process's whole
        // lifetime, so this is a lease renewed by repeated short heartbeats
        // (AttachFE) rather than a single persistent connection -- a lease
        // goes stale (and can be reclaimed by a different session) if it
        // isn't renewed within FeLeaseTimeout.
        private string leaseSessionId;
        private DateTime leaseLastSeen;
        private readonly object leaseLock = new object();
        public static readonly TimeSpan FeLeaseTimeout = TimeSpan.FromSeconds(15);

        public SubscriptionHub(ILocalProjectStore store)
        {
            this.store = store;
        }

        public ILocalProjectStore Store => store;

        /// <summary>
        /// This node's upstream, if any (null for a root node with no
        /// upstream). Settable after construction since UpstreamLink itself
        /// needs a reference back to this hub to ingest incoming frames.
        /// </summary>
        public UpstreamLink Upstream { get; set; }

        /// <summary>
        /// True if `projectPath` is available locally already, or becomes
        /// available by lazily subscribing through Upstream (blocking until
        /// that subscribe's initial replay completes, or fails). False if
        /// it's not local and there's no upstream, or the upstream doesn't
        /// have it either.
        /// </summary>
        public async Task<bool> EnsureAvailableAsync(string projectPath)
        {
            if (store.HasProject(projectPath))
            {
                return true;
            }
            if (Upstream == null)
            {
                return false;
            }
            return await Upstream.EnsureSubscribedAsync(projectPath).ConfigureAwait(false);
        }

        /// <summary>
        /// Handle a subscribe request: replay the project's current state to
        /// `channel`, then register it to receive future updates. Assumes
        /// the caller has already ensured the project is available (e.g. via
        /// EnsureAvailableAsync) -- this only checks the local store.
        /// </summary>
        public async Task<SubscribeResult> SubscribeAsync(string projectPath, ISubscriberChannel channel)
        {
            if (!store.HasProject(projectPath))
            {
                return new SubscribeResult { Success = false, ErrorMessage = $"unknown project \"{projectPath}\"" };
            }

            store.TryReplayProject(projectPath, out ProjectSnapshot snapshot);

            if (snapshot.ProjectInfoJson != null)
            {
                await channel.SendProjectInfoAsync(projectPath, snapshot.ProjectInfoJson).ConfigureAwait(false);
            }
            foreach (KeyDataEntry entry in snapshot.KeyData)
            {
                await channel.SendKeyDataAsync(entry.KeyDataPath, entry.Json, entry.BinData).ConfigureAwait(false);
            }
            // States are replayed after KeyData so that any KeyData path a
            // state references is already resident by the time it arrives.
            foreach (StateEntry entry in snapshot.States)
            {
                await channel.SendStateAsync(entry.StatePath, entry.Json).ConfigureAwait(false);
            }
            await channel.SendUpdateAsync().ConfigureAwait(false);

            GetOrAddSubscriberSet(projectPath)[channel] = 0;

            return new SubscribeResult { Success = true };
        }

        /// <summary>
        /// Accept (or renew) `sessionId` as the one FE allowed to call
        /// SetActiveAsync right now. True if `sessionId` is already the
        /// holder, there is no current holder, or the current lease has
        /// gone stale (no renewal within FeLeaseTimeout) -- false if a
        /// different, still-fresh session already holds it.
        /// </summary>
        public bool TryAttachFE(string sessionId)
        {
            lock (leaseLock)
            {
                bool leaseIsStale = leaseSessionId == null || (DateTime.UtcNow - leaseLastSeen) > FeLeaseTimeout;
                if (leaseSessionId == null || leaseSessionId == sessionId || leaseIsStale)
                {
                    leaseSessionId = sessionId;
                    leaseLastSeen = DateTime.UtcNow;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Designate `statePath` (a full "Org/Dataset/State/Name" path) as
        /// this node's single active project+state, and push that out to
        /// every current "active" subscriber -- a fresh replay of the whole
        /// project if it's a different project than they were last showing,
        /// or just the newly-active state if it's the same project. Gated
        /// by the FE lease (see TryAttachFE) -- a setactive call implicitly
        /// claims/renews the lease for `sessionId`, same as an explicit
        /// AttachFE heartbeat would.
        /// </summary>
        public async Task<SubscribeResult> SetActiveAsync(string statePath, string sessionId)
        {
            if (!TryAttachFE(sessionId))
            {
                return new SubscribeResult { Success = false, ErrorMessage = "another FE is attached" };
            }

            string projectPath = ProjectPaths.GetProjectPath(statePath);
            if (!store.HasProject(projectPath))
            {
                return new SubscribeResult { Success = false, ErrorMessage = $"unknown project \"{projectPath}\"" };
            }

            SetActivePointer(statePath);
            await NotifyActiveWrappersAsync().ConfigureAwait(false);

            return new SubscribeResult { Success = true };
        }

        /// <summary>
        /// Validation-free active-pointer setter, used only by UpstreamLink
        /// when it learns of a new active designation from its upstream
        /// (which has already validated it on its own end) -- unlike
        /// SetActiveAsync, deliberately doesn't check store.HasProject,
        /// since UpstreamLink calls this before the corresponding
        /// project/keydata replay frames have necessarily finished
        /// ingesting locally.
        /// </summary>
        internal void SetActivePointer(string statePath)
        {
            activeProjectPath = ProjectPaths.GetProjectPath(statePath);
            activeStateName = ProjectPaths.GetName(statePath);
        }

        /// <summary>
        /// Pushes the current active project/state to every registered
        /// "active" subscriber. Called after SetActiveAsync validates a
        /// direct FE push, and by UpstreamLink once a chained replay burst
        /// finishes (by which point store.HasProject is guaranteed true).
        /// </summary>
        internal async Task NotifyActiveWrappersAsync()
        {
            foreach (ActiveSubscriberWrapper wrapper in activeWrappers.Values.ToList())
            {
                await ApplyActiveToWrapperAsync(wrapper).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// True if a project+state is (or becomes, by chaining upstream --
        /// see UpstreamLink.EnsureActiveKnownAsync) active on this node.
        /// Split out from SubscribeActiveAsync the same way
        /// EnsureAvailableAsync is split from SubscribeAsync: callers over
        /// the wire need to know whether to write an "ok" ack *before* the
        /// replay itself starts writing frames.
        /// </summary>
        public async Task<bool> EnsureActiveAvailableAsync()
        {
            if (!string.IsNullOrEmpty(activeProjectPath))
            {
                return true;
            }
            if (Upstream != null)
            {
                await Upstream.EnsureActiveKnownAsync().ConfigureAwait(false);
            }
            return !string.IsNullOrEmpty(activeProjectPath);
        }

        /// <summary>
        /// Replay whatever project+state is currently active to `channel`,
        /// then register it to keep receiving updates. Assumes the caller
        /// already confirmed availability (e.g. via
        /// EnsureActiveAvailableAsync) -- this only checks the local
        /// pointer. `channel` only ever receives State frames for whichever
        /// state is active at the time -- if the active designation later
        /// changes (even to a different project entirely), this subscriber
        /// transparently gets a fresh replay of the new one, no
        /// re-subscribe needed from the caller.
        /// </summary>
        public async Task<SubscribeResult> SubscribeActiveAsync(ISubscriberChannel channel)
        {
            if (string.IsNullOrEmpty(activeProjectPath))
            {
                return new SubscribeResult { Success = false, ErrorMessage = "no active project" };
            }

            var wrapper = new ActiveSubscriberWrapper(channel);
            activeWrappers[channel] = wrapper;
            await ApplyActiveToWrapperAsync(wrapper).ConfigureAwait(false);
            return new SubscribeResult { Success = true };
        }

        private async Task ApplyActiveToWrapperAsync(ActiveSubscriberWrapper wrapper)
        {
            if (wrapper.CurrentProjectPath != activeProjectPath)
            {
                if (wrapper.CurrentProjectPath != null)
                {
                    UnsubscribeChannel(wrapper);
                }
                wrapper.CurrentProjectPath = activeProjectPath;
                wrapper.ActiveStateName = activeStateName;
                await SubscribeAsync(activeProjectPath, wrapper).ConfigureAwait(false);
            }
            else
            {
                // Same project, just a (possibly newly-arrived) different
                // state within it -- resend only the one that's now active
                // rather than replaying the whole project again.
                wrapper.ActiveStateName = activeStateName;
                if (store.TryReplayProject(activeProjectPath, out ProjectSnapshot snapshot))
                {
                    StateEntry match = snapshot.States.Find(s => ProjectPaths.GetName(s.StatePath) == activeStateName);
                    if (match != null)
                    {
                        await wrapper.SendStateAsync(match.StatePath, match.Json).ConfigureAwait(false);
                        await wrapper.SendUpdateAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Wraps a real ISubscriberChannel, filtering State frames down to
        /// just the currently-active one (see ApplyActiveToWrapperAsync) so
        /// an "active" subscriber never receives styling for a state other
        /// than the one it's meant to be following.
        /// </summary>
        private class ActiveSubscriberWrapper : ISubscriberChannel
        {
            public readonly ISubscriberChannel Inner;
            public string CurrentProjectPath;
            public string ActiveStateName;

            public ActiveSubscriberWrapper(ISubscriberChannel inner)
            {
                Inner = inner;
            }

            public Task SendProjectInfoAsync(string projectPath, string json) => Inner.SendProjectInfoAsync(projectPath, json);
            public Task SendKeyDataAsync(string keyDataPath, string json, byte[] binData) => Inner.SendKeyDataAsync(keyDataPath, json, binData);
            public Task SendUpdateAsync() => Inner.SendUpdateAsync();

            public Task SendStateAsync(string statePath, string json)
            {
                if (ProjectPaths.GetName(statePath) != ActiveStateName)
                {
                    return Task.CompletedTask;
                }
                return Inner.SendStateAsync(statePath, json);
            }
        }

        /// <summary>Remove a subscriber from every project it was registered under (e.g. once its connection dies).</summary>
        public void Unsubscribe(ISubscriberChannel channel)
        {
            if (activeWrappers.TryRemove(channel, out ActiveSubscriberWrapper wrapper))
            {
                UnsubscribeChannel(wrapper);
                return;
            }
            UnsubscribeChannel(channel);
        }

        private void UnsubscribeChannel(ISubscriberChannel channel)
        {
            foreach (var subscribers in subscribersByProject.Values)
            {
                subscribers.TryRemove(channel, out _);
            }
        }

        /// <summary>Ingest a projectinfo write: persist it, mark the project dirty, and forward it live to existing subscribers.</summary>
        public async Task IngestProjectInfoAsync(string projectPath, string json)
        {
            store.SaveProjectInfo(projectPath, json);
            dirtyProjects[projectPath] = 0;
            await FanOutAsync(projectPath, channel => channel.SendProjectInfoAsync(projectPath, json)).ConfigureAwait(false);
        }

        /// <summary>Ingest a keydata write: persist it, mark its project dirty, and forward it live to existing subscribers.</summary>
        public async Task IngestKeyDataAsync(string keyDataPath, string json, byte[] binData)
        {
            store.SaveKeyData(keyDataPath, json, binData);
            string projectPath = ProjectPaths.GetProjectPath(keyDataPath);
            dirtyProjects[projectPath] = 0;
            await FanOutAsync(projectPath, channel => channel.SendKeyDataAsync(keyDataPath, json, binData)).ConfigureAwait(false);
        }

        /// <summary>Ingest a named visualization state write: persist it, mark its project dirty, and forward it live to existing subscribers.</summary>
        public async Task IngestStateAsync(string statePath, string json)
        {
            store.SaveState(statePath, json);
            string projectPath = ProjectPaths.GetProjectPath(statePath);
            dirtyProjects[projectPath] = 0;
            await FanOutAsync(projectPath, channel => channel.SendStateAsync(statePath, json)).ConfigureAwait(false);
        }

        /// <summary>Ingest a bare "update" barrier: forward it only to subscribers of projects that actually changed since the last one.</summary>
        public async Task IngestUpdateAsync()
        {
            List<string> dirty = new List<string>(dirtyProjects.Keys);
            foreach (string projectPath in dirty)
            {
                dirtyProjects.TryRemove(projectPath, out _);
                await FanOutAsync(projectPath, channel => channel.SendUpdateAsync()).ConfigureAwait(false);
            }
        }

        private ConcurrentDictionary<ISubscriberChannel, byte> GetOrAddSubscriberSet(string projectPath)
        {
            return subscribersByProject.GetOrAdd(projectPath, _ => new ConcurrentDictionary<ISubscriberChannel, byte>());
        }

        private async Task FanOutAsync(string projectPath, System.Func<ISubscriberChannel, Task> send)
        {
            if (!subscribersByProject.TryGetValue(projectPath, out var subscribers) || subscribers.IsEmpty)
            {
                return;
            }

            // Snapshot the current subscriber set: a channel that dies mid-fan-out
            // is dropped by the caller (DataStoreServer) via Unsubscribe, not here.
            foreach (ISubscriberChannel channel in subscribers.Keys)
            {
                try
                {
                    await send(channel).ConfigureAwait(false);
                }
                catch
                {
                    subscribers.TryRemove(channel, out _);
                }
            }
        }
    }
}
