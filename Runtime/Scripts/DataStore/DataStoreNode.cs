/* DataStoreNode.cs
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

namespace IVLab.ABREngine.DataStore
{
    /// <summary>Address of another DataStore node to act as this one's upstream.</summary>
    public class RemoteEndpoint
    {
        public string Host;
        public int Port;
    }

    public class DataStoreNodeConfig
    {
        /// <summary>Port to accept subscribe/listprojects/ingestion connections on.</summary>
        public int ListenPort;

        /// <summary>Directory to persist projects under (created if missing).</summary>
        public string LocalStorageRoot;

        /// <summary>Directory to persist VisAssets under (created if missing).</summary>
        public string VisAssetStorageRoot;

        /// <summary>
        /// Optional upstream DataStore to lazily subscribe through on a
        /// local cache miss. Null (default) means this is a root node --
        /// only ParaView or other direct pushes feed it.
        /// </summary>
        public RemoteEndpoint Upstream;
    }

    /// <summary>
    /// Top-level facade for one DataStore node: owns local storage, the
    /// subscription hub, and the listening server. This is the entry point
    /// both the standalone console app (DataStore/Program.cs) and, in a
    /// later phase, ABREngine itself construct to run a node.
    /// </summary>
    public class DataStoreNode
    {
        public SubscriptionHub Hub { get; private set; }

        private DataStoreServer server;

        public void Start(DataStoreNodeConfig config)
        {
            var store = new FilesystemProjectStore(config.LocalStorageRoot);
            var visAssetStore = new FilesystemVisAssetStore(config.VisAssetStorageRoot);
            Hub = new SubscriptionHub(store, visAssetStore);
            if (config.Upstream != null)
            {
                Hub.Upstream = new UpstreamLink(config.Upstream.Host, config.Upstream.Port, Hub);
            }
            server = new DataStoreServer(config.ListenPort, Hub);
            server.Start();
        }

        public void Stop()
        {
            server?.Stop();
            server = null;
        }
    }
}
