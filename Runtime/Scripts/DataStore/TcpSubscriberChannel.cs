/* TcpSubscriberChannel.cs
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

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// Writes DataStore protocol frames to a subscribed connection's stream.
    /// Writes are serialized with a semaphore because SubscriptionHub can
    /// fan out to the same channel concurrently from independent ingestion
    /// tasks (e.g. a projectinfo and a keydata write landing at the same
    /// time) -- without this, concurrent writers would interleave bytes
    /// mid-frame and corrupt the stream.
    /// </summary>
    public class TcpSubscriberChannel : ISubscriberChannel
    {
        private readonly Stream stream;
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

        public TcpSubscriberChannel(Stream stream)
        {
            this.stream = stream;
        }

        public async Task SendProjectInfoAsync(string projectPath, string json)
        {
            await writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await FrameIO.WriteStringAsync(stream, ProtocolTags.ProjectInfo).ConfigureAwait(false);
                await FrameIO.WriteStringAsync(stream, projectPath).ConfigureAwait(false);
                await FrameIO.WriteStringAsync(stream, json).ConfigureAwait(false);
                await FrameIO.WriteBytesAsync(stream, System.Array.Empty<byte>()).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }

        public async Task SendKeyDataAsync(string keyDataPath, string json, byte[] binData)
        {
            await writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await FrameIO.WriteStringAsync(stream, ProtocolTags.KeyData).ConfigureAwait(false);
                await FrameIO.WriteStringAsync(stream, keyDataPath).ConfigureAwait(false);
                await FrameIO.WriteStringAsync(stream, json).ConfigureAwait(false);
                await FrameIO.WriteBytesAsync(stream, binData ?? System.Array.Empty<byte>()).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }

        public async Task SendUpdateAsync()
        {
            await writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await FrameIO.WriteStringAsync(stream, ProtocolTags.Update).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }
    }
}
