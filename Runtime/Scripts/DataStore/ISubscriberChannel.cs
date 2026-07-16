/* ISubscriberChannel.cs
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

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// A live downstream connection subscribed to one project -- either a
    /// Unity client's socket or (in a future phase) another chained
    /// DataStore's upstream connection. SubscriptionHub pushes frames
    /// through this interface without needing to know what's on the other
    /// end. A thrown exception from any Send*Async call means the channel
    /// is dead and SubscriptionHub will deregister it.
    /// </summary>
    public interface ISubscriberChannel
    {
        Task SendProjectInfoAsync(string projectPath, string json);
        Task SendKeyDataAsync(string keyDataPath, string json, byte[] binData);
        Task SendUpdateAsync();
    }
}
