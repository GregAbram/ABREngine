/* TestSubscriber.cs
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
using System.Net.Sockets;
using System.Threading.Tasks;
using IVLab.ABREngine.DataStore;

/// <summary>
/// Minimal CLI client used to verify DataStoreServer end-to-end: subscribes
/// to a project and prints the full-state replay followed by any live
/// updates, or lists the projects a running DataStore node knows about.
/// </summary>
internal static class TestSubscriber
{
    public static async Task SubscribeAsync(string host, int port, string projectPath)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.Subscribe).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, projectPath).ConfigureAwait(false);

        string ack = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (ack.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"Subscribe failed: {ack}");
            return;
        }

        Console.WriteLine($"Subscribed to \"{projectPath}\". Waiting for frames (Ctrl+C to stop)...");

        while (true)
        {
            string tag;
            try
            {
                tag = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Console.WriteLine("Connection closed by server.");
                return;
            }

            switch (tag)
            {
                case ProtocolTags.ProjectInfo:
                    {
                        string label = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
                        Console.WriteLine($"[projectinfo] {label}: {json}");
                    }
                    break;

                case ProtocolTags.KeyData:
                    {
                        string label = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        byte[] bindata = await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
                        Console.WriteLine($"[keydata] {label}: {json.Length} json chars, {bindata.Length} binary bytes");
                    }
                    break;

                case ProtocolTags.Update:
                    Console.WriteLine("[update] -- end of replay / live flush --");
                    break;

                default:
                    Console.WriteLine($"Unexpected tag \"{tag}\" -- stopping.");
                    return;
            }
        }
    }

    public static async Task ListProjectsAsync(string host, int port)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.ListProjects).ConfigureAwait(false);

        string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"listprojects failed: {status}");
            return;
        }

        int count = await FrameIO.ReadInt32Async(stream).ConfigureAwait(false);
        Console.WriteLine($"{count} project(s):");
        for (int i = 0; i < count; i++)
        {
            string projectPath = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            Console.WriteLine($"  {projectPath}");
        }
    }
}
