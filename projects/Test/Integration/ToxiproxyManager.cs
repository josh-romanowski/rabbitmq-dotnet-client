﻿using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Test;
using Toxiproxy.Net;
using Toxiproxy.Net.Toxics;

namespace Integration
{
    public class ToxiproxyManager : IAsyncDisposable
    {
        private const string ProxyNamePrefix = "rmq";
        private const ushort ProxyPortStart = 55669;
        private static int s_proxyPort = ProxyPortStart;

        private readonly string _testDisplayName;
        private readonly int _proxyPort;
        private readonly Connection _proxyConnection;
        private readonly Client _proxyClient;
        private readonly Proxy _proxy;

        private bool _disposedValue = false;

        public ToxiproxyManager(string testDisplayName, bool isRunningInCI, bool isWindows)
        {
            if (string.IsNullOrWhiteSpace(testDisplayName))
            {
                throw new ArgumentNullException(nameof(testDisplayName));
            }

            _testDisplayName = testDisplayName;

            _proxyPort = Interlocked.Increment(ref s_proxyPort);

            /*
             * Note:
             * Do NOT set resetAllToxicsAndProxiesOnClose to true, because it will
             * clear proxies being used by parallel TFM test runs
             */
            _proxyConnection = new Connection(resetAllToxicsAndProxiesOnClose: false);
            _proxyClient = _proxyConnection.Client();

            // to start, assume everything is on localhost
            _proxy = new Proxy
            {
                Enabled = true,
                Listen = $"{IPAddress.Loopback}:{_proxyPort}",
                Upstream = $"{IPAddress.Loopback}:5672",
            };

            if (isRunningInCI)
            {
                _proxy.Listen = $"0.0.0.0:{_proxyPort}";

                // GitHub Actions
                if (false == isWindows)
                {
                    /*
                     * Note: See the following setup script:
                     * .ci/ubuntu/gha-setup.sh
                     */
                    _proxy.Upstream = "rabbitmq-dotnet-client-rabbitmq:5672";
                }
            }
        }

        public int ProxyPort => _proxyPort;

        public async Task InitializeAsync()
        {
            string proxyName = $"{ProxyNamePrefix}-{_testDisplayName}-{Util.Now}-{Guid.NewGuid()}";
            _proxy.Name = proxyName;

            ushort retryCount = 5;
            do
            {
                try
                {
                    await _proxyClient.AddAsync(_proxy);
                    return;
                }
                catch (Exception ex)
                {
                    if (retryCount == 0)
                    {
                        throw;
                    }
                    else
                    {
                        string now = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                        Console.Error.WriteLine("{0} [ERROR] error initializing proxy '{1}': {2}", now, proxyName, ex);
                    }
                }
                --retryCount;
                await Task.Delay(TimeSpan.FromSeconds(1));
            } while (retryCount >= 0);
        }

        public Task<T> AddToxicAsync<T>(T toxic) where T : ToxicBase
        {
            return _proxy.AddAsync(toxic);
        }

        public Task RemoveToxicAsync(string toxicName)
        {
            return _proxy.RemoveToxicAsync(toxicName);
        }

        public Task EnableAsync()
        {
            _proxy.Enabled = true;
            return _proxyClient.UpdateAsync(_proxy);
        }

        public Task DisableAsync()
        {
            _proxy.Enabled = false;
            return _proxyClient.UpdateAsync(_proxy);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposedValue)
            {
                try
                {
                    await _proxyClient.DeleteAsync(_proxy);
                    _proxyConnection.Dispose();
                }
                catch (Exception ex)
                {
                    string now = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                    Console.Error.WriteLine("{0} [ERROR] error disposing proxy '{1}': {2}", now, _proxy.Name, ex);
                }
                finally
                {
                    _disposedValue = true;
                }
            }
        }
    }
}
