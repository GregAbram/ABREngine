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
    public class DataStoreNodeConfig
    {
        /// <summary>Port to accept subscribe/listprojects/ingestion connections on.</summary>
        public int ListenPort;

        /// <summary>Directory to persist projects under (created if missing).</summary>
        public string LocalStorageRoot;

        // Reserved for a later phase: an optional upstream (host, port) to
        // lazily subscribe through on a local cache miss. Phase 1 is
        // single-node only, so there is nothing to configure here yet.
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
            Hub = new SubscriptionHub(store);
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
