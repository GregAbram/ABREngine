/* FrameIO.cs
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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// Plain System.IO/System.Threading reimplementation of the frame
    /// primitives in IVLab.Utilities.StreamMethods, byte-layout-identical
    /// (4-byte big-endian length prefix followed by that many raw bytes;
    /// strings are UTF-8 bytes framed the same way). Exists so this folder
    /// has no dependency on the Unity-only IVLab.Utilities package, since it
    /// must also compile as a plain .NET console app.
    /// </summary>
    public static class FrameIO
    {
        public static async Task<int> ReadInt32Async(Stream s, CancellationToken cancelToken = default)
        {
            byte[] intBytes = new byte[4];
            int totalReceived = 0;
            while (totalReceived < intBytes.Length)
            {
                int received = await s.ReadAsync(intBytes, totalReceived, intBytes.Length - totalReceived, cancelToken).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new EndOfStreamException("Stream closed while reading a 4-byte length prefix");
                }
                totalReceived += received;
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToInt32(intBytes, 0);
        }

        public static async Task WriteInt32Async(Stream s, int value, CancellationToken cancelToken = default)
        {
            byte[] intBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(intBytes);
            }
            await s.WriteAsync(intBytes, 0, intBytes.Length, cancelToken).ConfigureAwait(false);
        }

        public static async Task<byte[]> ReadBytesAsync(Stream s, CancellationToken cancelToken = default)
        {
            int numBytes = await ReadInt32Async(s, cancelToken).ConfigureAwait(false);
            byte[] data = new byte[numBytes];
            int totalReceived = 0;
            while (totalReceived < numBytes)
            {
                int received = await s.ReadAsync(data, totalReceived, numBytes - totalReceived, cancelToken).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new EndOfStreamException("Stream closed while reading a byte payload");
                }
                totalReceived += received;
            }
            return data;
        }

        public static async Task WriteBytesAsync(Stream s, byte[] data, CancellationToken cancelToken = default)
        {
            await WriteInt32Async(s, data.Length, cancelToken).ConfigureAwait(false);
            if (data.Length > 0)
            {
                await s.WriteAsync(data, 0, data.Length, cancelToken).ConfigureAwait(false);
            }
        }

        public static async Task<string> ReadStringAsync(Stream s, CancellationToken cancelToken = default)
        {
            byte[] data = await ReadBytesAsync(s, cancelToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(data);
        }

        public static async Task WriteStringAsync(Stream s, string text, CancellationToken cancelToken = default)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            await WriteBytesAsync(s, data, cancelToken).ConfigureAwait(false);
        }
    }
}
