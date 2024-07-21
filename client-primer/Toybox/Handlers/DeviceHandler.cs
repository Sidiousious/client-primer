using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Data;
using GagSpeak.Services.Mediator;
using GagSpeak.UI;
using ImGuiNET;
using NAudio.Gui;
using OtterGui;
using System.Numerics;

namespace GagSpeak.PlayerData.Handlers;

/// <summary>
/// handles the connected devices and the socket connection to the Intiface server.
/// </summary>
public class DeviceHandler : DisposableMediatorSubscriberBase
{
    // likely will include API controller and other things later. Otherwise they will be in ToyboxHandler.
    private readonly ClientConfigurationManager _clientConfigs;
    private readonly UiSharedService _uiShared;
    private readonly DeviceFactory _deviceFactory;

    private ButtplugClient ButtplugClient;
    public ButtplugWebsocketConnector WebsocketConnector;
    private CancellationTokenSource? BatteryCheckCTS = new();

    private readonly List<ConnectedDevice> Devices = new List<ConnectedDevice>();
    private readonly Dictionary<string, int> ActiveDeviceAndMotors = new Dictionary<string, int>();

    // maybe store triggers here in the future, or in the trigger handler, but not now.

    public DeviceHandler(ILogger<DeviceHandler> logger, GagspeakMediator mediator,
        ClientConfigurationManager clientConfiguration, UiSharedService uiShared,
        DeviceFactory deviceFactory) : base(logger, mediator)
    {
        _clientConfigs = clientConfiguration;
        _uiShared = uiShared;
        _deviceFactory = deviceFactory;

        // create the websocket connector
        WebsocketConnector = NewWebsocketConnection();
        // initialize the client
        ButtplugClient = new ButtplugClient(IntifaceClientName);

        // subscribe to the events we should subscribe to, and attach them to our mediator subscriber
        ButtplugClient.DeviceAdded += (sender, args) => Mediator.Publish(new ToyDeviceAdded(args.Device));
        ButtplugClient.DeviceRemoved += (sender, args) => Mediator.Publish(new ToyDeviceRemoved(args.Device));
        ButtplugClient.ScanningFinished += (sender, args) => Mediator.Publish(new ToyScanFinished());
        ButtplugClient.ServerDisconnect += (sender, args) => Mediator.Publish(new ButtplugClientDisconnected());

        // subscribe to our mediator events
        Mediator.Subscribe<ToyDeviceAdded>(this, (msg) => OnDeviceAdded(msg));

        Mediator.Subscribe<ToyDeviceRemoved>(this, (msg) => OnDeviceRemoved(msg));

        Mediator.Subscribe<ToyScanFinished>(this, (msg) => OnScanningFinished());

        Mediator.Subscribe<ButtplugClientDisconnected>(this, (msg) => OnButtplugClientDisconnected());
    }

    // public accessors.
    public const string IntifaceClientName = "GagSpeak Vibe-Service";
    public bool ConnectedToIntiface => ButtplugClient != null && ButtplugClient.Connected;
    public bool AnyDeviceConnected => ButtplugClient.Connected && ButtplugClient.Devices.Any();
    public int ConnectedDevicesCount => Devices.Count;
    public bool ScanningForDevices { get; private set; }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Ensure ButtplugClient is not null before trying to unsubscribe and dispose
        if (ButtplugClient != null)
        {
            // Unsubscribe from events
            ButtplugClient.DeviceAdded -= (sender, args) => Mediator.Publish(new ToyDeviceAdded(args.Device));
            ButtplugClient.DeviceRemoved -= (sender, args) => Mediator.Publish(new ToyDeviceRemoved(args.Device));
            ButtplugClient.ScanningFinished -= (sender, args) => Mediator.Publish(new ToyScanFinished());
            ButtplugClient.ServerDisconnect -= (sender, args) => Mediator.Publish(new ButtplugClientDisconnected());

            // Disconnect and dispose ButtplugClient
            ButtplugClient.DisconnectAsync().Wait();
            ButtplugClient.Dispose();
            // dispose the connector
            WebsocketConnector.Dispose();
        }
        // cancel the battery check token
        BatteryCheckCTS?.Cancel();
    }

    public void DrawDevicesTable()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X * 0.3f, 4));
        using var table = ImRaii.Table("ConnectedDevices", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, );
        if (!table) { return; }

        var refX = ImGui.GetCursorPos();
        ImGui.TableSetupColumn("Device Name", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 125f);
        ImGui.TableSetupColumn("Display Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Vibrates", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Vibrates.").X);
        ImGui.TableSetupColumn("Rotates", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Rotates.").X);
        ImGui.TableSetupColumn("Linear", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Linear.").X);
        ImGui.TableSetupColumn("Oscillates", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Oscillates.").X);
        ImGui.TableSetupColumn("%##BatteryPercent", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("100%").X);
        ImGui.TableHeadersRow();        

        foreach (var device in Devices)
        {
            ImGui.TableNextColumn();
            ImGui.Text(device.DeviceName);
            ImGui.TableNextColumn();
            var displayName = device.DisplayName;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if(ImGui.InputTextWithHint($"##DisplayName{device.DeviceName}", "Public Name..", 
                ref displayName, 48, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                device.DisplayName = displayName;
            }
            ImGui.Text(device.DisplayName);
            ImGui.TableNextColumn();
            _uiShared.BooleanToColoredIcon(device.CanVibrate, false);
            ImGui.TableNextColumn();
            _uiShared.BooleanToColoredIcon(device.CanRotate, false);
            ImGui.TableNextColumn();
            _uiShared.BooleanToColoredIcon(device.CanLinear, false);
            ImGui.TableNextColumn();
            _uiShared.BooleanToColoredIcon(device.CanOscillate, false);
            ImGui.TableNextColumn();
            ImGui.Text($"{device.BatteryPercentString()}");
        }
    }


    private ButtplugWebsocketConnector NewWebsocketConnection()
    {
        return _clientConfigs.GagspeakConfig.IntifaceConnectionSocket != null
                    ? new ButtplugWebsocketConnector(new Uri($"{_clientConfigs.GagspeakConfig.IntifaceConnectionSocket}"))
                    : new ButtplugWebsocketConnector(new Uri("ws://localhost:12345"));
    }

    #region EventHandling
    // handles event where device is added to Intiface Central
    private void OnDeviceAdded(ToyDeviceAdded msg)
    {
        try
        {
            // use our factory to create the new device
            ConnectedDevice newDevice = _deviceFactory.CreateConnectedDevice(msg.Device);
            // set that it is sucessfully connected and append it
            newDevice.IsConnected = true;
            Devices.Add(newDevice);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error adding device to device list. {ex.Message}");
        }
    }

    private void OnDeviceRemoved(ToyDeviceRemoved msg)
    {
        try
        {
            // find the device in the list and remove it
            int IndexInDeviceListToRemove = Devices.FindIndex((ConnectedDevice device) => device.DeviceId == msg.Device.Index);
            // see if the index is valid.
            if (IndexInDeviceListToRemove > -1)
            {
                // log the removal and remove it
                Logger.LogInformation($"Device {Devices[IndexInDeviceListToRemove]} removed from device list.");
                // create shallow copy
                ConnectedDevice device2 = Devices[IndexInDeviceListToRemove];
                // remove from list
                Devices.RemoveAt(IndexInDeviceListToRemove);
                // disconnect.
                device2.IsConnected = false;
                // we call in thos order so that if it ever fails to disconnect, it will be caught in the
                // try catch block, and still be marked as connected.
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error removing device from device list. {ex.Message}");
        }
    }

    /// <summary> Fired when scanning for devices is finished </summary>
    private void OnScanningFinished()
    {
        Logger.LogInformation("Finished Scanning for new Devices");
        ScanningForDevices = false;
    }

    private void OnButtplugClientDisconnected()
    {
        Logger.LogInformation("Intiface Central Disconnected");
        HandleDisconnect();
    }

    #endregion EventHandling


    #region ConnectionHandle
    public async void ConnectToIntifaceAsync()
    {
        try
        {
            // if we satisfy any conditions to refuse connection, early return
            if (ButtplugClient == null)
            {
                Logger.LogError("ButtplugClient is null. Cannot connect to Intiface Central");
                return;
            }
            else if (ButtplugClient.Connected)
            {
                Logger.LogInformation("Already connected to Intiface Central");
                return;
            }
            else if (WebsocketConnector == null)
            {
                Logger.LogError("WebsocketConnector is null. Cannot connect to Intiface Central");
                return;
            }
            if (ConnectedToIntiface)
            {
                Logger.LogInformation("Already connected to Intiface Central");
                return;
            }
            // Attempt connection to server
            Logger.LogDebug("Attempting connection to Intiface Central");
            await ButtplugClient.ConnectAsync(WebsocketConnector);
        }
        catch (ButtplugClientConnectorException socketEx)
        {
            Logger.LogError($"Error Connecting to Websocket. Is your Intiface Opened? | {socketEx.Message}");
            DisconnectFromIntifaceAsync();
            return;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error connecting to Intiface Central (Potentially timed out?) | {ex}");
            DisconnectFromIntifaceAsync();
            return;
        }

        // see if we sucessfully connected
        Logger.LogInformation("Connected to Intiface Central");
        try
        {
            // scan for any devices for the next 2 seconds
            Logger.LogInformation("Scanning for devices over the next 2 seconds.");
            await StartDeviceScanAsync();
            Thread.Sleep(2000);
            await StopDeviceScanAsync();

            // Reason to connect is valid, so reset the battery check token
            BatteryCheckCTS?.Cancel();
            BatteryCheckCTS?.Dispose();
            BatteryCheckCTS = new CancellationTokenSource();
            _ = BatteryHealthCheck(BatteryCheckCTS.Token);

            // see if we managed to fetch any devices
            if (AnyDeviceConnected)
            {
                // if we did, and that device had a stored intensity, set the intensity on that device.
                // TODO: Implement this logic.
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error scanning for devices after connecting to Intiface Central. {ex}");
        }
    }

    public async void DisconnectFromIntifaceAsync()
    {
        try
        {
            // see if we are currently conected to the server.
            if (ButtplugClient.Connected)
            {
                // if we are, disconnect.
                await ButtplugClient.DisconnectAsync();
                // if the disconnect was sucessful, handle the disconnect.
                if (!ButtplugClient.Connected)
                {
                    Logger.LogInformation("Disconnected from Intiface Central");
                    // no need to use handleDisconnect here since we execute that in the subscribed event.
                }
            }
            // recreate the websocket connector
            WebsocketConnector.Dispose();
            WebsocketConnector = NewWebsocketConnection();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error disconnecting from Intiface Central. {ex}");
        }
    }

    public void HandleDisconnect()
    {
        Logger.LogDebug("Client was properly disconnected from Intiface Central. Disconnecting Device Handler.");
        try
        {
            Devices.Clear();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error clearing devices from device list. {ex.Message}");
        }

        // do not dispose of the client once disconnected, we want to stay linked so that we can reconnect faster.
        BatteryCheckCTS?.Cancel();
    }

    #endregion ConnectionHandle
    
    /// <summary> 
    /// Asyncronous method that continuously checks the battery health of the client 
    /// until canceled at a set interval
    /// </summary>
    private async Task BatteryHealthCheck(CancellationToken ct)
    {
        // while the cancellation token is not requested and the hub is not null
        while (!ct.IsCancellationRequested && ConnectedToIntiface)
        {
            // wait for 60 seconds. The longer between checks, the better on a toys battery life.
            await Task.Delay(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            // log that we are checking the client health state
            Logger.LogTrace("Scheduled Battery Check on connected devices");
            // if we need to reconnect, break out of the loop
            if (!ConnectedToIntiface) break;
            // we can perform the check, so fetch battery from all devices
            try
            {
                foreach (ConnectedDevice device in Devices)
                    device.UpdateBatteryPercentage();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error while fetching the battery level from devices: {ex.Message}");
            }
        }
    }


    /// <summary> Start scanning for devices asynchronously </summary>
    public async Task StartDeviceScanAsync()
    {
        // begin scan if we are connected
        if (!ButtplugClient.Connected)
        {
            Logger.LogWarning("Cannot scan for devices if not connected to Intiface Central");
        }

        Logger.LogDebug("Now actively scanning for new devices...");
        try
        {
            ScanningForDevices = true;
            await ButtplugClient.StartScanningAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error in ScanForDevicesAsync: {ex.ToString()}");
        }
    }

    /// <summary> Stop scanning for devices asynchronously </summary>
    public async Task StopDeviceScanAsync()
    {
        // stop the scan if we are connected
        if (!ButtplugClient.Connected)
        {
            Logger.LogWarning("Cannot stop scanning for devices if not connected to Intiface Central");
        }

        Logger.LogDebug("Halting the scan for new devices to add");
        try
        {
            await ButtplugClient.StopScanningAsync();
            if (ScanningForDevices)
            {
                ScanningForDevices = false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error in StopScanForDevicesAsync: {ex.ToString()}");
        }
    }


}
