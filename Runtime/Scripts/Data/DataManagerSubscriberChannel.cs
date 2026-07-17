/* DataManagerSubscriberChannel.cs
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

using System.Threading.Tasks;
using UnityEngine;
using IVLab.Utilities;
using IVLab.ABREngine.DataStore;

namespace IVLab.ABREngine
{
    /// <summary>
    /// An ISubscriberChannel that feeds frames received from a DataStore
    /// subscription (both the initial full-state replay and any live
    /// updates afterward) directly into this Unity instance's DataManager
    /// and rendering pipeline -- mirroring exactly what
    /// <see cref="SocketDataListener"/> does for a direct ParaView push.
    /// The only difference is the data arrives via a DataStore subscription
    /// (SubscriptionHub.SubscribeAsync) instead of a raw producer
    /// connection, so unlike SocketDataListener this does not also call
    /// DataManager.CacheRawDataset -- the DataStore's own local project
    /// store already persists this data to the same shared storage root
    /// (see ABREngine.Awake's DataStoreNodeConfig.LocalStorageRoot).
    /// </summary>
    internal class DataManagerSubscriberChannel : ISubscriberChannel
    {
        public Task SendProjectInfoAsync(string projectPath, string json)
        {
            ProjectInfo info = JsonUtility.FromJson<ProjectInfo>(json);
            ABREngine.Instance.Data.SaveProjectInfo(projectPath, info.bounds);
            return Task.CompletedTask;
        }

        public Task SendKeyDataAsync(string keyDataPath, string json, byte[] binData)
        {
            RawDataset.JsonHeader header = JsonUtility.FromJson<RawDataset.JsonHeader>(json);
            RawDataset dataset = new RawDataset(header, binData);

            // Unload the outdated version of the dataset, same as SocketDataListener.
            if (ABREngine.Instance.Data.TryGetRawDataset(keyDataPath, out RawDataset _existing))
            {
                ABREngine.Instance.Data.UnloadRawDataset(keyDataPath);
            }

            ABREngine.Instance.Data.ImportRawDataset(keyDataPath, dataset);

            foreach (var idi in ABREngine.Instance.GetDataImpressions(keyDataPath))
            {
                idi.RenderHints.DataChanged = true;
            }

            return Task.CompletedTask;
        }

        public Task SendStateAsync(string statePath, string json)
        {
            // A project can have multiple named states; this instance only
            // applies the one named by Config.subscribeStateName (empty =
            // data-only mode, ignore every state).
            if (ProjectPaths.GetName(statePath) != ABREngine.Instance.Config.subscribeStateName)
            {
                return Task.CompletedTask;
            }

            return UnityThreadScheduler.Instance.RunMainThreadWork(() =>
                ABREngine.Instance.LoadState<TextStateFileLoader>(json));
        }

        public Task SendUpdateAsync()
        {
            return UnityThreadScheduler.Instance.RunMainThreadWork(() => ABREngine.Instance.Render());
        }
    }
}
