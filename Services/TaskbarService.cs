using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MasseterDrop.Helpers;
using Point = System.Windows.Point;

namespace MasseterDrop.Services
{
    public class TaskbarService : IDisposable
    {
        private const string PipeName = "MasseterDrop_IPC_SingleInstance";
        private CancellationTokenSource? _pipeCts;
        private bool _isDisposed = false;

        public event Action? ActivationRequested;

        public void StartSingleInstanceServer()
        {
            _pipeCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_pipeCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.InOut,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync(_pipeCts.Token);

                        using var reader = new StreamReader(server);
                        using var writer = new StreamWriter(server) { AutoFlush = true };

                        string? command = await reader.ReadLineAsync();
                        if (command == "TOGGLE" || command == "ACTIVATE")
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                ActivationRequested?.Invoke();
                            });
                            await writer.WriteLineAsync("ACK");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Named pipe server error: {ex.Message}");
                        await Task.Delay(500, _pipeCts.Token);
                    }
                }
            });
        }

        public static bool SignalExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                client.Connect(1500); // Wait up to 1.5 seconds

                using var writer = new StreamWriter(client) { AutoFlush = true };
                using var reader = new StreamReader(client);

                writer.WriteLine("TOGGLE");
                string? response = reader.ReadLine();
                return response == "ACK";
            }
            catch
            {
                return false;
            }
        }

        public static Point CalculatePanelPosition(double panelWidth, double panelHeight)
        {
            var workArea = SystemParameters.WorkArea;
            var taskbar = NativeMethods.GetTaskbarInfo();

            double targetLeft;
            double targetTop;
            const double margin = 12.0;

            if (taskbar.Exists)
            {
                switch (taskbar.Edge)
                {
                    case NativeMethods.ABE_TOP:
                        targetLeft = workArea.Right - panelWidth - margin;
                        targetTop = workArea.Top + margin;
                        break;

                    case NativeMethods.ABE_LEFT:
                        targetLeft = workArea.Left + margin;
                        targetTop = workArea.Bottom - panelHeight - margin;
                        break;

                    case NativeMethods.ABE_RIGHT:
                        targetLeft = workArea.Right - panelWidth - margin;
                        targetTop = workArea.Bottom - panelHeight - margin;
                        break;

                    case NativeMethods.ABE_BOTTOM:
                    default:
                        targetLeft = workArea.Right - panelWidth - margin;
                        targetTop = workArea.Bottom - panelHeight - margin;
                        break;
                }
            }
            else
            {
                // Default bottom-right corner inside work area
                targetLeft = workArea.Right - panelWidth - margin;
                targetTop = workArea.Bottom - panelHeight - margin;
            }

            // Ensure window stays completely within screen bounds
            if (targetLeft + panelWidth > workArea.Right)
            {
                targetLeft = workArea.Right - panelWidth - margin;
            }
            if (targetLeft < workArea.Left)
            {
                targetLeft = workArea.Left + margin;
            }

            if (targetTop + panelHeight > workArea.Bottom)
            {
                targetTop = workArea.Bottom - panelHeight - margin;
            }
            if (targetTop < workArea.Top)
            {
                targetTop = workArea.Top + margin;
            }

            return new Point(targetLeft, targetTop);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_pipeCts != null)
            {
                _pipeCts.Cancel();
                _pipeCts.Dispose();
                _pipeCts = null;
            }
        }
    }
}
