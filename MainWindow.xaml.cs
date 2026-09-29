using System.IO.Ports;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FpvTelemetry;

public partial class MainWindow : Window
{
    private readonly MspV2Parser _parser = new();
    private readonly DispatcherTimer _simulationTimer;
    private readonly string[] _simulationValues = ["FCAT2", "FW 1.0.0", "BAT 12.47V", "STANDBY", "ARMING", "ARMED", "SAFE TIME 01:42", "DISARMED"];
    private SerialPort? _serialPort;
    private int _simulationIndex;
    private int _packetCount;
    private int _crcErrors;

    public MainWindow()
    {
        InitializeComponent();
        _parser.TextReceived += OnTextReceived;
        _parser.CrcError += () => Dispatcher.Invoke(() => { _crcErrors++; UpdateCounters(); });
        _simulationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        _simulationTimer.Tick += (_, _) => ShowTelemetry(_simulationValues[_simulationIndex++ % _simulationValues.Length], "SIM");
        RefreshPorts();
        Closed += (_, _) => CloseSerialPort();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        var selected = PortCombo.SelectedItem as string;
        PortCombo.ItemsSource = SerialPort.GetPortNames().OrderBy(x => x).ToArray();
        PortCombo.SelectedItem = selected;
        if (PortCombo.SelectedIndex < 0 && PortCombo.Items.Count > 0) PortCombo.SelectedIndex = 0;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_serialPort?.IsOpen == true) { CloseSerialPort(); return; }
        if (PortCombo.SelectedItem is not string portName)
        {
            MessageBox.Show("Оберіть COM-порт або ввімкніть режим імітації.", "UART", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            int baud = int.Parse(((System.Windows.Controls.ComboBoxItem)BaudCombo.SelectedItem).Content.ToString()!);
            _serialPort = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 500,
                DtrEnable = false,
                RtsEnable = false
            };
            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.Open();
            SetConnectionState(true, $"Підключено: {portName}");
            ConnectButton.Content = "Відключити";
        }
        catch (Exception ex)
        {
            CloseSerialPort();
            MessageBox.Show($"Не вдалося відкрити порт:\n{ex.Message}", "Помилка UART", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var port = (SerialPort)sender;
            int available = port.BytesToRead;
            if (available <= 0) return;
            var data = new byte[available];
            int read = port.Read(data, 0, data.Length);
            _parser.Feed(data.AsSpan(0, read));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddLog("UART error: " + ex.Message));
        }
    }

    private void OnTextReceived(string text) => Dispatcher.Invoke(() => ShowTelemetry(text, "UART"));

    private void ShowTelemetry(string text, string source)
    {
        TelemetryText.Text = string.IsNullOrWhiteSpace(text) ? "—" : text;
        LastUpdateText.Text = $"Оновлено {DateTime.Now:HH:mm:ss.fff}  •  {source}";
        _packetCount++;
        UpdateCounters();
        AddLog($"{DateTime.Now:HH:mm:ss}  {text}");
    }

    private void AddLog(string value)
    {
        LogList.Items.Insert(0, value);
        while (LogList.Items.Count > 100) LogList.Items.RemoveAt(LogList.Items.Count - 1);
    }

    private void UpdateCounters() => PacketCountText.Text = $"Коректних пакетів: {_packetCount}   •   Помилок CRC: {_crcErrors}";

    private void Simulation_Checked(object sender, RoutedEventArgs e)
    {
        CloseSerialPort();
        PortCombo.IsEnabled = BaudCombo.IsEnabled = RefreshButton.IsEnabled = ConnectButton.IsEnabled = false;
        _simulationIndex = 0;
        _simulationTimer.Start();
        SetConnectionState(true, "Імітація активна");
        ShowTelemetry(_simulationValues[_simulationIndex++], "SIM");
    }

    private void Simulation_Unchecked(object sender, RoutedEventArgs e)
    {
        _simulationTimer.Stop();
        PortCombo.IsEnabled = BaudCombo.IsEnabled = RefreshButton.IsEnabled = ConnectButton.IsEnabled = true;
        SetConnectionState(false, "Відключено");
    }

    private void CloseSerialPort()
    {
        if (_serialPort is not null)
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            if (_serialPort.IsOpen) _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
        }
        if (ConnectButton is not null) ConnectButton.Content = "Підключити";
        if (SimulationCheck?.IsChecked != true) SetConnectionState(false, "Відключено");
    }

    private void SetConnectionState(bool connected, string text)
    {
        StatusText.Text = text;
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(connected ? "#65E4A3" : "#EF5B5B"));
    }
}
