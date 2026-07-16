/* DataStoreServer.cs
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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// Accepts connections and dispatches DataStore protocol messages.
    /// Structurally mirrors Runtime/Scripts/Data/SocketDataListener.cs's
    /// accept-loop shape (accept -> handle each connection independently ->
    /// accept again), but as a clean async loop rather than the
    /// Begin/EndAcceptTcpClient callback style, and understands two more
    /// tags: "subscribe" (its connection is intentionally kept open for a
    /// live reply stream, unlike every other one-shot message type) and
    /// "listprojects".
    /// </summary>
    public class DataStoreServer
    {
        private readonly int port;
        private readonly SubscriptionHub hub;
        private TcpListener listener;
        private CancellationTokenSource cancelSource;
        private Task acceptLoopTask;

        public DataStoreServer(int port, SubscriptionHub hub)
        {
            this.port = port;
            this.hub = hub;
        }

        public void Start()
        {
            if (listener != null)
            {
                return;
            }

            listener = new TcpListener(IPAddress.Any, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();

            cancelSource = new CancellationTokenSource();
            acceptLoopTask = AcceptLoopAsync(cancelSource.Token);
        }

        public void Stop()
        {
            cancelSource?.Cancel();
            listener?.Stop();
            listener = null;
        }

        private async Task AcceptLoopAsync(CancellationToken cancelToken)
        {
            while (!cancelToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Listener was stopped -- exit the loop.
                    break;
                }

                _ = HandleConnectionAsync(client, cancelToken);
            }
        }

        private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancelToken)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                try
                {
                    string tag = await FrameIO.ReadStringAsync(stream, cancelToken).ConfigureAwait(false);

                    switch (tag)
                    {
                        case ProtocolTags.Update:
                            await hub.IngestUpdateAsync().ConfigureAwait(false);
                            await FrameIO.WriteStringAsync(stream, ProtocolTags.Ok, cancelToken).ConfigureAwait(false);
                            break;

                        case ProtocolTags.ProjectInfo:
                            {
                                string label = await FrameIO.ReadStringAsync(stream, cancelToken).ConfigureAwait(false);
                                string json = await FrameIO.ReadStringAsync(stream, cancelToken).ConfigureAwait(false);
                                await FrameIO.ReadBytesAsync(stream, cancelToken).ConfigureAwait(false); // always zero-length for projectinfo; consume to stay framed
                                await hub.IngestProjectInfoAsync(label, json).ConfigureAwait(false);
                                await FrameIO.WriteStringAsync(stream, ProtocolTags.Ok, cancelToken).ConfigureAwait(false);
                            }
                            break;

                        case ProtocolTags.ListProjects:
                            await HandleListProjectsAsync(stream, cancelToken).ConfigureAwait(false);
                            break;

                        case ProtocolTags.Subscribe:
                            await HandleSubscribeAsync(stream, cancelToken).ConfigureAwait(false);
                            break;

                        default:
                            // Any other tag is treated as a key data write, matching
                            // SocketDataListener's existing permissive convention.
                            {
                                string label = await FrameIO.ReadStringAsync(stream, cancelToken).ConfigureAwait(false);
                                string json = await FrameIO.ReadStringAsync(stream, cancelToken).ConfigureAwait(false);
                                byte[] bindata = await FrameIO.ReadBytesAsync(stream, cancelToken).ConfigureAwait(false);
                                await hub.IngestKeyDataAsync(label, json, bindata).ConfigureAwait(false);
                                await FrameIO.WriteStringAsync(stream, ProtocolTags.Ok, cancelToken).ConfigureAwait(false);
                            }
                            break;
                    }
                }
                catch (Exception e)
                {
                    try
                    {
                        await FrameIO.WriteStringAsync(stream, ProtocolTags.ErrorPrefix + e.Message, cancelToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Connection is already dead -- nothing more to do.
                    }
                }
            }
        }

        private async Task HandleListProjectsAsync(NetworkStream stream, CancellationToken cancelToken)
        {
            var projects = hub.Store.ListLocalProjects();
            // Phase 1: single-node only, so the local set is the complete
            // answer. A later phase extends this to first request the
            // upstream's list (if configured) and overlay these local
            // entries on top of it, so an upstream entry always survives.
            await FrameIO.WriteStringAsync(stream, ProtocolTags.Ok, cancelToken).ConfigureAwait(false);
            await FrameIO.WriteInt32Async(stream, projects.Count, cancelToken).ConfigureAwait(false);
            foreach (string projectPath in projects)
            {
                await FrameIO.WriteStringAsync(stream, projectPath, cancelToken).ConfigureAwait(false);
            }
        }

        private async Task HandleSubscribeAsync(NetworkStream stream, CancellationToken cancelToken)
        {
            string projectPath = await FrameIO.ReadStringAsync(stream, cancelToken).ConfigureAwait(false);

            var channel = new TcpSubscriberChannel(stream);
            SubscribeResult result;
            try
            {
                bool exists = hub.Store.HasProject(projectPath);
                if (!exists)
                {
                    await FrameIO.WriteStringAsync(stream, ProtocolTags.ErrorPrefix + $"unknown project \"{projectPath}\"", cancelToken).ConfigureAwait(false);
                    return;
                }

                await FrameIO.WriteStringAsync(stream, ProtocolTags.Ok, cancelToken).ConfigureAwait(false);
                result = await hub.SubscribeAsync(projectPath, channel).ConfigureAwait(false);
            }
            catch
            {
                hub.Unsubscribe(channel);
                return;
            }

            if (!result.Success)
            {
                // Only reachable via a race between the HasProject check above
                // and SubscribeAsync's own check; nothing in Phase 1 removes a
                // project, so this is effectively unreachable today.
                return;
            }

            try
            {
                await WaitForDisconnectAsync(stream, cancelToken).ConfigureAwait(false);
            }
            finally
            {
                hub.Unsubscribe(channel);
            }
        }

        /// <summary>
        /// A subscribe connection has no v1 client-to-server messages after
        /// the initial request, so this just blocks until the peer closes
        /// (or the connection errors), to know when to deregister it.
        /// </summary>
        private static async Task WaitForDisconnectAsync(NetworkStream stream, CancellationToken cancelToken)
        {
            byte[] buffer = new byte[1];
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(buffer, 0, 1, cancelToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                }
            }
            catch
            {
                // Connection reset/aborted -- treat the same as a graceful close.
            }
        }
    }
}
