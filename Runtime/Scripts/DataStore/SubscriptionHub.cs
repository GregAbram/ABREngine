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

using System.Collections.Concurrent;
using System.Collections.Generic;
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

        /// <summary>Remove a subscriber from every project it was registered under (e.g. once its connection dies).</summary>
        public void Unsubscribe(ISubscriberChannel channel)
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
