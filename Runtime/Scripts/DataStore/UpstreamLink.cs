/* UpstreamLink.cs
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
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// Client role: this node's connection to its one upstream (either
    /// another DataStore, or -- from that upstream's point of view -- this
    /// node just looks like any other subscriber). Two responsibilities:
    ///
    /// 1. Lazy cold-subscribe: the first time a local subscriber asks for a
    ///    project this node doesn't have yet, open a "subscribe" connection
    ///    upstream, wait for the full-state replay to land (persisting it
    ///    locally and fanning it out to local subscribers as it arrives via
    ///    SubscriptionHub.Ingest*Async -- the same path used for direct
    ///    ParaView ingestion), then keep tailing that connection live.
    ///    Concurrent local requests for the same cold project are deduped
    ///    to a single upstream subscribe.
    /// 2. Recursive listprojects: forward a listprojects request upstream on
    ///    demand (DataStoreServer does the merging).
    /// </summary>
    public class UpstreamLink
    {
        private readonly string host;
        private readonly int port;
        private readonly SubscriptionHub hub;

        // Project path -> task that resolves true once that project's
        // upstream subscription has delivered its first "update" (replay
        // complete), or false if the upstream doesn't have it either. Also
        // serves as the in-flight dedup: a second caller for the same
        // project while one is already pending/warm gets the same task.
        private readonly ConcurrentDictionary<string, Task<bool>> subscriptions = new ConcurrentDictionary<string, Task<bool>>();

        public UpstreamLink(string host, int port, SubscriptionHub hub)
        {
            this.host = host;
            this.port = port;
            this.hub = hub;
        }

        /// <summary>
        /// Ensure this node is (or becomes) subscribed upstream to
        /// `projectPath`. Returns once the initial replay has landed
        /// locally (true), or once the upstream reports it doesn't have the
        /// project either (false).
        /// </summary>
        public Task<bool> EnsureSubscribedAsync(string projectPath)
        {
            return subscriptions.GetOrAdd(projectPath, SubscribeToUpstreamAsync);
        }

        private async Task<bool> SubscribeToUpstreamAsync(string projectPath)
        {
            TcpClient client = new TcpClient();
            NetworkStream stream;
            try
            {
                await client.ConnectAsync(host, port).ConfigureAwait(false);
                stream = client.GetStream();

                await FrameIO.WriteStringAsync(stream, ProtocolTags.Subscribe).ConfigureAwait(false);
                await FrameIO.WriteStringAsync(stream, projectPath).ConfigureAwait(false);

                string ack = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                if (ack.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
                {
                    client.Dispose();
                    subscriptions.TryRemove(projectPath, out _);
                    return false;
                }
            }
            catch
            {
                client.Dispose();
                subscriptions.TryRemove(projectPath, out _);
                return false;
            }

            // Read the replay (and, indefinitely after, the live tail) on a
            // background task; signal the caller as soon as the first
            // "update" (end-of-replay) frame lands.
            var becameWarm = new TaskCompletionSource<bool>();
            _ = Task.Run(() => ReadLoopAsync(projectPath, client, stream, becameWarm));
            return await becameWarm.Task.ConfigureAwait(false);
        }

        private async Task ReadLoopAsync(string projectPath, TcpClient client, NetworkStream stream, TaskCompletionSource<bool> becameWarm)
        {
            using (client)
            {
                bool signaledReplayComplete = false;
                try
                {
                    while (true)
                    {
                        string tag = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        switch (tag)
                        {
                            case ProtocolTags.ProjectInfo:
                                {
                                    string label = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                                    string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                                    await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
                                    await hub.IngestProjectInfoAsync(label, json).ConfigureAwait(false);
                                }
                                break;

                            case ProtocolTags.KeyData:
                                {
                                    string label = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                                    string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                                    byte[] bindata = await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
                                    await hub.IngestKeyDataAsync(label, json, bindata).ConfigureAwait(false);
                                }
                                break;

                            case ProtocolTags.State:
                                {
                                    string label = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                                    string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                                    await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
                                    await hub.IngestStateAsync(label, json).ConfigureAwait(false);
                                }
                                break;

                            case ProtocolTags.Update:
                                await hub.IngestUpdateAsync().ConfigureAwait(false);
                                if (!signaledReplayComplete)
                                {
                                    signaledReplayComplete = true;
                                    becameWarm.TrySetResult(true);
                                }
                                break;

                            default:
                                // Protocol violation from upstream -- give up on this link.
                                return;
                        }
                    }
                }
                catch
                {
                    // Connection dropped (network hiccup, upstream restart, etc).
                }
                finally
                {
                    if (!signaledReplayComplete)
                    {
                        becameWarm.TrySetResult(false);
                    }
                    // Drop the dedup entry so a future cold access re-subscribes
                    // rather than being stuck waiting on a dead link forever.
                    subscriptions.TryRemove(projectPath, out _);
                }
            }
        }

        /// <summary>Request the upstream's known project list. Returns empty (not an error) if the upstream is unreachable.</summary>
        public async Task<IReadOnlyCollection<string>> RequestListProjectsAsync()
        {
            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(host, port).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();

                await FrameIO.WriteStringAsync(stream, ProtocolTags.ListProjects).ConfigureAwait(false);

                string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
                {
                    return Array.Empty<string>();
                }

                int count = await FrameIO.ReadInt32Async(stream).ConfigureAwait(false);
                var result = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    result.Add(await FrameIO.ReadStringAsync(stream).ConfigureAwait(false));
                }
                return result;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
