/* Program.cs
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
using System.Threading;
using System.Threading.Tasks;
using IVLab.ABREngine.DataStore;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        switch (args[0])
        {
            case "serve":
                return RunServe(args);
            case "subscribe":
                return await RunSubscribeAsync(args).ConfigureAwait(false);
            case "listprojects":
                return await RunListProjectsAsync(args).ConfigureAwait(false);
            default:
                PrintUsage();
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  DataStore serve <port> <storageRoot>");
        Console.Error.WriteLine("  DataStore subscribe <host> <port> <projectPath>");
        Console.Error.WriteLine("  DataStore listprojects <host> <port>");
    }

    private static int RunServe(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[1], out int port))
        {
            PrintUsage();
            return 1;
        }
        string storageRoot = args[2];

        var node = new DataStoreNode();
        node.Start(new DataStoreNodeConfig { ListenPort = port, LocalStorageRoot = storageRoot });
        Console.WriteLine($"DataStore listening on port {port}, storing under \"{storageRoot}\". Press Ctrl+C to stop.");

        var stopSignal = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopSignal.Set();
        };
        stopSignal.Wait();

        node.Stop();
        return 0;
    }

    private static async Task<int> RunSubscribeAsync(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[2], out int port))
        {
            PrintUsage();
            return 1;
        }
        string host = args[1];
        string projectPath = args[3];

        await TestSubscriber.SubscribeAsync(host, port, projectPath).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunListProjectsAsync(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], out int port))
        {
            PrintUsage();
            return 1;
        }
        string host = args[1];

        await TestSubscriber.ListProjectsAsync(host, port).ConfigureAwait(false);
        return 0;
    }
}
