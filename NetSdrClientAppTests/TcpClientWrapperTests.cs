using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetSdrClientApp.Networking;
using NUnit.Framework;

namespace NetSdrClientApp.Tests.Networking
{
    public class TestableTcpClientWrapper : TcpClientWrapper
    {
        public TestableTcpClientWrapper(string host, int port) : base(host, port) { }

        public Task CallStartListeningAsync()
        {
            return base.StartListeningAsync();
        }

        public bool IsConnected => base.Connected;
    }


    [TestFixture]
    public class TcpClientWrapperTests
    {
        private TcpListener? _testServer;
        private int _testPort;
        private CancellationTokenSource? _serverCts;

        [SetUp]
        public void SetUp()
        {
            _testPort = GetAvailablePort();
            _testServer = new TcpListener(IPAddress.Loopback, _testPort);
            _serverCts = new CancellationTokenSource();
        }

        [TearDown]
        public void TearDown()
        {
            _serverCts?.Cancel();
            _serverCts?.Dispose();
            _testServer?.Stop();

            if (_testServer is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _testServer = null;
            _serverCts = null;
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task AcceptClientAsync(TcpListener server, CancellationToken ct)
        {
            try
            {
                var client = await server.AcceptTcpClientAsync(ct);
                await Task.Delay(500, ct);
                client.Close();
            }
            catch (OperationCanceledException) { }
        }

        private static async Task AcceptAndReceiveAsync(TcpListener server, TaskCompletionSource<byte[]> receivedData, CancellationToken ct)
        {
            try
            {
                var client = await server.AcceptTcpClientAsync(ct);
                var stream = client.GetStream();
                byte[] buffer = new byte[1024];
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                receivedData.SetResult(buffer.Take(bytesRead).ToArray());
                await Task.Delay(100, ct);
                client.Close();
            }
            catch (OperationCanceledException) { }
        }

        [Test]
        public void Constructor_SetsHostAndPort()
        {
            var wrapper = new TcpClientWrapper("localhost", 8080);
            Assert.That(wrapper, Is.Not.Null);
            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public void Connected_ReturnsFalse_WhenNotConnected()
        {
            var wrapper = new TcpClientWrapper("localhost", 8080);
            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public async Task Connect_EstablishesConnection_WhenServerIsAvailable()
        {
            Assert.That(_testServer, Is.Not.Null);
            _testServer.Start();
            var serverTask = AcceptClientAsync(_testServer, _serverCts!.Token);
            var wrapper = new TcpClientWrapper("localhost", _testPort);

            wrapper.Connect();
            await Task.Delay(100);

            Assert.That(wrapper.Connected, Is.True);

            wrapper.Disconnect();
            await serverTask;
        }

        [Test]
        public void Connect_DoesNothing_WhenAlreadyConnected()
        {
            Assert.That(_testServer, Is.Not.Null);
            Assert.That(_serverCts, Is.Not.Null);

            _testServer.Start();
            var serverTask = AcceptClientAsync(_testServer, _serverCts.Token);
            var wrapper = new TcpClientWrapper("localhost", _testPort);
            wrapper.Connect();
            Task.Delay(100).Wait();

            wrapper.Connect();

            Assert.That(wrapper.Connected, Is.True);

            wrapper.Disconnect();
        }

        [Test]
        public void Connect_HandlesConnectionFailure_WhenServerNotAvailable()
        {
            var wrapper = new TcpClientWrapper("localhost", 9999);

            wrapper.Connect();
            Task.Delay(100).Wait();

            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public async Task Disconnect_ClosesConnection_WhenConnected()
        {
            Assert.That(_testServer, Is.Not.Null);
            Assert.That(_serverCts, Is.Not.Null);

            _testServer.Start();
            var serverTask = AcceptClientAsync(_testServer, _serverCts.Token);
            var wrapper = new TcpClientWrapper("localhost", _testPort);
            wrapper.Connect();
            await Task.Delay(100);

            wrapper.Disconnect();

            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public void Disconnect_DoesNothing_WhenNotConnected()
        {
            var wrapper = new TcpClientWrapper("localhost", _testPort);
            wrapper.Disconnect();
            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public async Task SendMessageAsync_ByteArray_SendsData_WhenConnected()
        {
            Assert.That(_testServer, Is.Not.Null);
            Assert.That(_serverCts, Is.Not.Null);

            _testServer.Start();
            var receivedData = new TaskCompletionSource<byte[]>();
            var serverTask = AcceptAndReceiveAsync(_testServer, receivedData, _serverCts.Token);

            var wrapper = new TcpClientWrapper("localhost", _testPort);
            wrapper.Connect();
            await Task.Delay(100);

            byte[] testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            await wrapper.SendMessageAsync(testData);

            var received = await receivedData.Task;
            Assert.That(received, Is.EqualTo(testData));

            wrapper.Disconnect();
        }

        [Test]
        public async Task SendMessageAsync_String_SendsData_WhenConnected()
        {
            Assert.That(_testServer, Is.Not.Null);
            Assert.That(_serverCts, Is.Not.Null);

            _testServer.Start();
            var receivedData = new TaskCompletionSource<byte[]>();
            var serverTask = AcceptAndReceiveAsync(_testServer, receivedData, _serverCts.Token);

            var wrapper = new TcpClientWrapper("localhost", _testPort);
            wrapper.Connect();
            await Task.Delay(100);

            string testString = "Hello, World!";
            byte[] expectedData = Encoding.UTF8.GetBytes(testString);

            await wrapper.SendMessageAsync(testString);

            var received = await receivedData.Task;
            Assert.That(received, Is.EqualTo(expectedData));

            wrapper.Disconnect();
        }

        [Test]
        public void SendMessageAsync_ByteArray_ThrowsException_WhenNotConnected()
        {
            var wrapper = new TcpClientWrapper("localhost", _testPort);
            byte[] testData = new byte[] { 0x01, 0x02, 0x03 };

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await wrapper.SendMessageAsync(testData)
            );
        }

        [Test]
        public void SendMessageAsync_String_ThrowsException_WhenNotConnected()
        {
            var wrapper = new TcpClientWrapper("localhost", _testPort);
            string testString = "Test";

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await wrapper.SendMessageAsync(testString)
            );
        }

        [Test]
        public async Task MessageReceived_Event_RaisedWhenDataReceived()
        {
            Assert.That(_testServer, Is.Not.Null);
            _testServer.Start();
            var wrapper = new TcpClientWrapper("localhost", _testPort);

            byte[]? receivedMessage = null;
            var messageReceivedEvent = new TaskCompletionSource<bool>();

            wrapper.MessageReceived += (sender, data) =>
            {
                receivedMessage = data;
                messageReceivedEvent.TrySetResult(true);
            };

            var serverTask = Task.Run(async () =>
            {
                var client = await _testServer!.AcceptTcpClientAsync();
                await Task.Delay(200);
                var stream = client.GetStream();
                byte[] testData = new byte[] { 0xAA, 0xBB, 0xCC };
                await stream.WriteAsync(testData.AsMemory(0, testData.Length));
                await Task.Delay(100);
                client.Close();
            });

            wrapper.Connect();
            await Task.WhenAny(messageReceivedEvent.Task, Task.Delay(2000));

            Assert.That(receivedMessage, Is.Not.Null);
            Assert.That(receivedMessage, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }));

            wrapper.Disconnect();
            await serverTask;
        }

        [Test]
        public async Task MultipleMessages_AreReceivedCorrectly()
        {
            Assert.That(_testServer, Is.Not.Null);
            _testServer.Start();
            var wrapper = new TcpClientWrapper("localhost", _testPort);

            var messagesReceived = new System.Collections.Concurrent.ConcurrentBag<byte[]>();
            var messageCount = new TaskCompletionSource<bool>();
            int expectedMessages = 3;

            wrapper.MessageReceived += (sender, data) =>
            {
                messagesReceived.Add(data);
                if (messagesReceived.Count >= expectedMessages)
                {
                    messageCount.TrySetResult(true);
                }
            };

            var serverTask = Task.Run(async () =>
            {
                var client = await _testServer!.AcceptTcpClientAsync();
                await Task.Delay(200);
                var stream = client.GetStream();

                for (int i = 1; i <= expectedMessages; i++)
                {
                    byte[] testData = new byte[] { (byte)i };
                    await stream.WriteAsync(testData.AsMemory(0, testData.Length));
                    await Task.Delay(50);
                }

                await Task.Delay(100);
                client.Close();
            });

            wrapper.Connect();
            await Task.WhenAny(messageCount.Task, Task.Delay(3000));

            Assert.That(messagesReceived, Has.Count.EqualTo(expectedMessages));

            wrapper.Disconnect();
            await serverTask;
        }

        [Test]
        public async Task Disconnect_StopsListening()
        {
            Assert.That(_testServer, Is.Not.Null);
            _testServer.Start();
            var wrapper = new TcpClientWrapper("localhost", _testPort);
            bool messageReceived = false;

            wrapper.MessageReceived += (sender, data) =>
            {
                messageReceived = true;
            };

            var serverTask = Task.Run(async () =>
            {
                var client = await _testServer!.AcceptTcpClientAsync();
                await Task.Delay(200);
                return client;
            });

            wrapper.Connect();
            await Task.Delay(100);
            var serverClient = await serverTask;

            wrapper.Disconnect();
            await Task.Delay(100);

            try
            {
                var stream = serverClient.GetStream();
                byte[] data = new byte[] { 0x01 };
                await stream.WriteAsync(data.AsMemory(0, data.Length));
            }
            catch { }

            await Task.Delay(200);

            Assert.That(messageReceived, Is.False);
            Assert.That(wrapper.Connected, Is.False);

            serverClient?.Close();
        }

        [Test]
        public async Task Listening_HandlesSocketError_Gracefully()
        {
            Assert.That(_testServer, Is.Not.Null);
            _testServer.Start();
            var wrapper = new TcpClientWrapper("localhost", _testPort);

            wrapper.Connect();
            await Task.Delay(100);
            Assert.That(wrapper.Connected, Is.True);

            _testServer.Stop();

            await Task.Delay(300);

            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public void CleanupResources_HandlesObjectDisposedException_ForCts()
        {
            var wrapper = new TcpClientWrapper("localhost", _testPort);

            wrapper.Connect();
            wrapper.Disconnect();

            wrapper.Disconnect();

            Assert.That(wrapper.Connected, Is.False);
        }

        // Ïîêðèâàº if (!Connected) ó StartListeningAsync
        [Test]
        public void StartListeningAsync_ThrowsException_IfNotConnected()
        {
            var wrapper = new TestableTcpClientWrapper("localhost", _testPort);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await wrapper.CallStartListeningAsync();
            });
        }

        // Ïîêðèâàº else if (bytesRead == 0)
        [Test]
        public async Task Listening_HandlesServerDisconnect_BytesReadZero()
        {
            Assert.That(_testServer, Is.Not.Null);
            _testServer.Start();
            var wrapper = new TcpClientWrapper("localhost", _testPort);

            wrapper.Connect();
            await Task.Delay(100);
            Assert.That(wrapper.Connected, Is.True);

            var serverTask = Task.Run(async () =>
            {
                var client = await _testServer!.AcceptTcpClientAsync();
                await Task.Delay(200);
                client.Close();
            });

            var timeoutTask = Task.Delay(2000);
            while (wrapper.Connected && timeoutTask.IsCompleted == false)
            {
                await Task.Delay(50);
            }

            await serverTask;

            Assert.That(wrapper.Connected, Is.False, "Wrapper should be disconnected after server closes connection (bytesRead == 0).");
        }
      
        [Test]
        public async Task Listening_HandlesGeneralSocketException()
        {
            Assert.That(_testServer, Is.Not.Null);
            _testServer.Start();
            var wrapper = new TcpClientWrapper("localhost", _testPort);

            var listenerStopped = new TaskCompletionSource<bool>();

            var listenerTask = Task.Run(async () =>
            {
                try
                {
                    wrapper.Connect();
                    await Task.Delay(100);
                    Assert.That(wrapper.Connected, Is.True);

                    var acceptedClient = await _testServer!.AcceptTcpClientAsync();
                    await Task.Delay(100);

                    acceptedClient.Client.Shutdown(SocketShutdown.Both);
                    acceptedClient.Close();

                  
                    var timeoutTask = Task.Delay(3000);
                    while (wrapper.Connected && timeoutTask.IsCompleted == false)
                    {
                        await Task.Delay(50);
                    }
                }
                catch (Exception) { }
                finally
                {
                    listenerStopped.TrySetResult(true);
                }
            });

            await Task.WhenAny(listenerStopped.Task, Task.Delay(3500)); 

            Assert.That(listenerStopped.Task.IsCompletedSuccessfully, Is.True, "Listener task should complete successfully after handling exception.");
            Assert.That(wrapper.Connected, Is.False, "Wrapper should be disconnected after socket error.");
        }
    }
}
