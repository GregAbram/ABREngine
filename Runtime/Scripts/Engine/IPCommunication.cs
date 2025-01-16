using System;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading;

namespace IVLab.ABREngine
{
    public class MyStream
    {
        private TcpClient client;
        private NetworkStream stream;
        protected void _read(ref byte[] bytes, int n)
        {
            int k = 0;
            while (k < n)
                k += stream.Read(bytes, k, n - k);
        }
        protected void _write(byte[] bytes, int n)
        {
            stream.Write(bytes, 0, n);
        }
        public int ReadInt()
        {
            byte[] bi = new byte[4];
            _read(ref bi, 4);
            Int32 n = System.BitConverter.ToInt32(bi, 0);
            return (int)n;
        }
        public byte[] ReadBytes()
        {
            int n = ReadInt();
            byte[] bytes = new byte[n];
            _read(ref bytes, n);
            return bytes;
        }
        public string ReadString()
        {
            byte[] bytes = ReadBytes();
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        public void SendInt(int n)
        {
            byte[] bytes = System.BitConverter.GetBytes(n);
            _write(bytes, bytes.Length);
        }
        public void SendBytes(byte[] bytes)
        {
            SendInt(bytes.Length);
            _write(bytes, bytes.Length);
        }
        public void SendString(string str)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            SendBytes(bytes);
        }
        public MyStream(string host, int port)
        {
            try
            {
                client = new(host, port);
                stream = client.GetStream();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        public MyStream(TcpClient c)
        {
            client = c;
            stream = client.GetStream();
        }
    }

    public class MyServer
    {
        private int _port;
        private Thread _myServerThread = null;
        public virtual void handler(MyStream client)
        {
            Console.WriteLine("default handler");
        }
        private void _serverThread()
        {
            TcpListener server = new(IPAddress.Any, _port);
            server.Start();

            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                MyStream stream = new(client);
                Thread thread = new Thread(() => handler(stream));
                thread.Start();
            }
        }
        public MyServer(int port)
        {
            _port = port;
        }
        public void Start()
        {
            _myServerThread = new Thread(() => _serverThread());
            _myServerThread.Start();
        }
        public void Wait()
        {
            _myServerThread.Join();
        }
    }
}
