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

                case ProtocolTags.State:
                    {
                        string label = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
                        Console.WriteLine($"[state] {label}: {json}");
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

    public static async Task ListStatesAsync(string host, int port, string projectPath)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.ListStates).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, projectPath).ConfigureAwait(false);

        string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"liststates failed: {status}");
            return;
        }

        int count = await FrameIO.ReadInt32Async(stream).ConfigureAwait(false);
        Console.WriteLine($"{count} state(s) for \"{projectPath}\":");
        for (int i = 0; i < count; i++)
        {
            string name = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            Console.WriteLine($"  {name}");
        }
    }

    public static async Task ListKeyDataAsync(string host, int port, string projectPath)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.ListKeyData).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, projectPath).ConfigureAwait(false);

        string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"listkeydata failed: {status}");
            return;
        }

        int count = await FrameIO.ReadInt32Async(stream).ConfigureAwait(false);
        Console.WriteLine($"{count} key data object(s) for \"{projectPath}\":");
        for (int i = 0; i < count; i++)
        {
            string name = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            Console.WriteLine($"  {name}: {json}");
        }
    }

    public static async Task SubscribeActiveAsync(string host, int port)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.SubscribeActive).ConfigureAwait(false);

        string ack = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (ack.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"subscribeactive failed: {ack}");
            return;
        }

        Console.WriteLine("Subscribed to whatever's active. Waiting for frames (Ctrl+C to stop)...");

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

                case ProtocolTags.State:
                    {
                        string label = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
                        await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
                        Console.WriteLine($"[active state] {label}: {json}");
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

    public static async Task SetActiveAsync(string host, int port, string statePath, string sessionId)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.SetActive).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, statePath).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, sessionId).ConfigureAwait(false);

        string ack = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        Console.WriteLine($"setactive \"{statePath}\" (session \"{sessionId}\") -> {ack}");
    }

    public static async Task AttachFEAsync(string host, int port, string sessionId)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.AttachFE).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, sessionId).ConfigureAwait(false);

        string ack = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        Console.WriteLine($"attachfe (session \"{sessionId}\") -> {ack}");
    }

    public static async Task GetStateAsync(string host, int port, string statePath)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.GetState).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, statePath).ConfigureAwait(false);

        string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"getstate failed: {status}");
            return;
        }

        string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        Console.WriteLine($"getstate \"{statePath}\": {json}");
    }

    public static async Task GetHistogramAsync(string host, int port, string keyDataPath, string variableLabel, int bins)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.GetHistogram).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, keyDataPath).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, variableLabel).ConfigureAwait(false);
        await FrameIO.WriteInt32Async(stream, bins).ConfigureAwait(false);

        string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"gethistogram failed: {status}");
            return;
        }

        string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        Console.WriteLine($"gethistogram \"{keyDataPath}\" ({variableLabel}, {bins} bins): {json}");
    }

    public static async Task ListVisAssetsAsync(string host, int port)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.ListVisAssets).ConfigureAwait(false);

        string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"listvisassets failed: {status}");
            return;
        }

        int count = await FrameIO.ReadInt32Async(stream).ConfigureAwait(false);
        Console.WriteLine($"{count} VisAsset(s):");
        for (int i = 0; i < count; i++)
        {
            string uuid = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            Console.WriteLine($"  {uuid}: {json}");
        }
    }

    public static async Task GetVisAssetAsync(string host, int port, string uuid)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.GetVisAsset).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, uuid).ConfigureAwait(false);

        string status = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        if (status.StartsWith(ProtocolTags.ErrorPrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"getvisasset failed: {status}");
            return;
        }

        string json = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        int fileCount = await FrameIO.ReadInt32Async(stream).ConfigureAwait(false);
        Console.WriteLine($"getvisasset \"{uuid}\": {json}");
        for (int i = 0; i < fileCount; i++)
        {
            string fileName = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
            byte[] fileData = await FrameIO.ReadBytesAsync(stream).ConfigureAwait(false);
            Console.WriteLine($"  {fileName}: {fileData.Length} bytes");
        }
    }

    public static async Task PushVisAssetAsync(string host, int port, string uuid, string json, System.Collections.Generic.Dictionary<string, byte[]> files)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.VisAsset).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, uuid).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, json).ConfigureAwait(false);
        await FrameIO.WriteInt32Async(stream, files.Count).ConfigureAwait(false);
        foreach (var file in files)
        {
            await FrameIO.WriteStringAsync(stream, file.Key).ConfigureAwait(false);
            await FrameIO.WriteBytesAsync(stream, file.Value).ConfigureAwait(false);
        }

        string ack = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        Console.WriteLine($"pushvisasset \"{uuid}\" ({files.Count} file(s)) -> {ack}");
    }

    public static async Task PushStateAsync(string host, int port, string statePath, string json)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        await FrameIO.WriteStringAsync(stream, ProtocolTags.State).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, statePath).ConfigureAwait(false);
        await FrameIO.WriteStringAsync(stream, json).ConfigureAwait(false);
        await FrameIO.WriteBytesAsync(stream, Array.Empty<byte>()).ConfigureAwait(false);

        string ack = await FrameIO.ReadStringAsync(stream).ConfigureAwait(false);
        Console.WriteLine($"pushstate \"{statePath}\" -> {ack}");
    }
}
