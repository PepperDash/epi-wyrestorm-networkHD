using System;
using System.Collections.Generic;
using System.Linq;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;
using PepperDash.Essentials.Plugin.Comms;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdGlobalRouter : EssentialsDevice, IRoutingNumeric, IMatrixRouting
{
    private static readonly NhdGlobalRouter _instance = new();

    public const string InstanceKey = "NhdRouter";
    public const string RouteOff = "$off";
    public const string NoSourceText = "No Source";

    private NhdGlobalRouter()
        : base(InstanceKey)
    {
        InputPorts = new RoutingPortCollection<RoutingInputPort>();
        OutputPorts = new RoutingPortCollection<RoutingOutputPort>();

        InputSlots = new Dictionary<string, IRoutingInputSlot>();
        OutputSlots = new Dictionary<string, IRoutingOutputSlot>();

        AddPostActivationAction(BuildMatrixRouting);
        AddPostActivationAction(BuildTieLines);
    }

    public static NhdGlobalRouter Instance => _instance;

    public RoutingPortCollection<RoutingInputPort> InputPorts { get; private set; }
    public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; private set; }

    public Dictionary<string, IRoutingInputSlot> InputSlots { get; private set; }
    public Dictionary<string, IRoutingOutputSlot> OutputSlots { get; private set; }

    public void ExecuteSwitch(object inputSelector, object outputSelector, eRoutingSignalType signalType)
    {
        if (outputSelector is not NhdMatrixOutput output)
        {
            this.LogError("Output selector is not NhdMatrixOutput");
            return;
        }

        if (inputSelector is not IRoutingInputSlot inputSlot)
        {
            this.LogError("Input selector is not IRoutingInputSlot");
            return;
        }

        var matrixCommand = BuildMatrixSetCommand(inputSlot, output, signalType);
        if (string.IsNullOrWhiteSpace(matrixCommand))
        {
            this.LogError("Unsupported signal type '{signalType}' for matrix command", signalType);
            return;
        }

        NhdApiCommandSender.TrySend(this, matrixCommand);

        if (signalType.HasFlag(eRoutingSignalType.Video))
            output.SetInputRoute(eRoutingSignalType.Video, inputSlot);

        if (signalType.HasFlag(eRoutingSignalType.Audio))
            output.SetInputRoute(eRoutingSignalType.Audio, inputSlot);
    }

    public void ExecuteNumericSwitch(ushort input, ushort output, eRoutingSignalType type)
    {
        throw new NotImplementedException("ExecuteNumericSwitch");
    }

    public void Route(string inputSlotKey, string outputSlotKey, eRoutingSignalType type)
    {
        if (!InputSlots.TryGetValue(inputSlotKey, out var inputSlot))
        {
            this.LogError("Unable to find input slot with key {0}", inputSlotKey);
            return;
        }

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find output slot with key {0}", outputSlotKey);
            return;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            Debug.LogMessage(Serilog.Events.LogEventLevel.Error, "Output with key {key} is not NhdMatrixOutput", this, outputSlotKey);
            return;
        }

        ExecuteSwitch(inputSlot, output, type);
    }

    public bool ActivateMultiviewLayout(string outputSlotKey, string layoutName)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview preset layouts", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview layout activation");
            return false;
        }

        return ctl.SessionManager.TryActivateMultiviewLayout(this, output.Device, layoutName);
    }

    public bool TryGetTrackedMultiviewLayout(string outputSlotKey, out string layoutName, out bool inferred)
    {
        layoutName = null;
        inferred = false;

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview preset layouts", output.Device.Key);
            return false;
        }

        layoutName = output.Device.ActivePresetMultiviewLayoutName;
        inferred = output.Device.ActivePresetMultiviewLayoutInferred;
        return !string.IsNullOrWhiteSpace(layoutName);
    }

    public bool ProbeAndLearnMultiviewLayouts(string outputSlotKey)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview preset layouts", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview layout probing");
            return false;
        }

        return ctl.SessionManager.TryProbeAndLearnMultiviewLayouts(this, output.Device);
    }

    public bool RouteMultiviewTile(string inputSlotKey, string outputSlotKey, int tileReference)
    {
        return RouteMultiviewTile(inputSlotKey, outputSlotKey, null, tileReference);
    }

    public bool FullscreenMultiviewTile(string outputSlotKey, int sourceTileReference)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview fullscreen");
            return false;
        }

        return ctl.SessionManager.TryFullscreenMultiviewTile(this, output.Device, sourceTileReference);
    }

    public bool ReturnFromMultiviewFullscreen(string outputSlotKey)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview fullscreen return");
            return false;
        }

        return ctl.SessionManager.TryReturnFromMultiviewFullscreen(this, output.Device);
    }

    public bool TryGetMultiviewFullscreenReturnLayout(string outputSlotKey, out string layoutName)
    {
        layoutName = null;

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview fullscreen return query");
            return false;
        }

        return ctl.SessionManager.TryGetMultiviewFullscreenReturnLayout(output.Device, out layoutName);
    }

    public bool RouteMultiviewTile(string inputSlotKey, string outputSlotKey, string layoutName, int tileReference)
    {
        if (!InputSlots.TryGetValue(inputSlotKey, out var inputSlot))
        {
            this.LogError("Unable to find multiview input slot with key {0}", inputSlotKey);
            return false;
        }

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (inputSlot is not NhdMatrixInput input)
        {
            this.LogError("Input slot with key {0} is not NhdMatrixInput", inputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview tile routing", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for guarded multiview tile routing");
            return false;
        }

        var sentImmediately = ctl.SessionManager.TryRouteMultiviewTile(this, input.Device, output.Device, layoutName, tileReference);

        if (!sentImmediately)
        {
            this.LogInformation(
                "Multiview tile route queued pending state verification. tx='{tx}', rx='{rx}', layout='{layout}', tile={tile}",
                input.Device.Key,
                output.Device.Key,
                string.IsNullOrWhiteSpace(layoutName) ? output.Device.ActivePresetMultiviewLayoutName : layoutName,
                tileReference);
        }

        return sentImmediately;
    }

    private void BuildMatrixRouting()
    {
        try
        {
            InputSlots = DeviceManager
                .AllDevices.OfType<NhdBaseDevice>()
                .Where(d => d.IsTransmitter)
                .Select(d => new NhdMatrixInput(d))
                .Cast<IRoutingInputSlot>()
                .ToDictionary(i => i.Key, i => i);

            var clearInput = new NhdMatrixClearInput();
            InputSlots.Add(clearInput.Key, clearInput);

            this.LogDebug("Total inputs: {count}", InputSlots.Count);

            OutputSlots = DeviceManager
                .AllDevices.OfType<NhdBaseDevice>()
                .Where(d => !d.IsTransmitter)
                .Select(d => new NhdMatrixOutput(d))
                .Cast<IRoutingOutputSlot>()
                .ToDictionary(o => o.Key, o => o);

            this.LogDebug("Total outputs: {count}", OutputSlots.Count);

            // Build router ports so tie lines can connect to them
            foreach (var tx in DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => d.IsTransmitter))
            {
                InputPorts.Add(new RoutingInputPort(
                    tx.Key,
                    eRoutingSignalType.AudioVideo,
                    eRoutingPortConnectionType.Streaming,
                    tx,
                    this));
            }

            foreach (var rx in DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => !d.IsTransmitter))
            {
                OutputPorts.Add(new RoutingOutputPort(
                    rx.Key,
                    eRoutingSignalType.AudioVideo,
                    eRoutingPortConnectionType.Streaming,
                    rx,
                    this));
            }
        }
        catch (Exception ex)
        {
            Debug.LogMessage(ex, "Exception building MatrixRouting: {message}", this, ex.Message);
        }
    }

    private static void BuildTieLines()
    {
        try
        {
            var transmitters = DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => d.IsTransmitter).ToList();
            NhdTieLineConnector.AddTieLinesForTransmitters(transmitters);

            var receivers = DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => !d.IsTransmitter).ToList();
            NhdTieLineConnector.AddTieLinesForReceivers(receivers);
        }
        catch (Exception ex)
        {
            Debug.LogMessage(ex, "Exception building tie lines: {message}", null, ex.Message);
        }
    }

    private static string BuildMatrixSetCommand(IRoutingInputSlot inputSlot, NhdMatrixOutput outputSlot, eRoutingSignalType signalType)
    {
        var rxRef = outputSlot.Device.ApiEndpointReference;
        var txRef = inputSlot is NhdMatrixInput matrixInput
            ? matrixInput.Device.ApiEndpointReference
            : "null";

        string prefix;
        if (signalType == eRoutingSignalType.AudioVideo)
        {
            prefix = "matrix set";
        }
        else if (signalType == eRoutingSignalType.Video)
        {
            prefix = "matrix video set";
        }
        else if (signalType == eRoutingSignalType.Audio)
        {
            prefix = "matrix audio set";
        }
        else if (signalType.HasFlag(NhdRoutingSignalTypes.Ir))
        {
            prefix = "matrix infrared set";
        }
        else if (signalType.HasFlag(NhdRoutingSignalTypes.Serial))
        {
            prefix = "matrix serial set";
        }
        else if (
            signalType.HasFlag(NhdRoutingSignalTypes.UsbInput)
            || signalType.HasFlag(NhdRoutingSignalTypes.UsbOutput)
            || signalType.HasFlag(eRoutingSignalType.Usb))
        {
            prefix = "matrix usb set";
        }
        else
        {
            return null;
        }

        return string.Format("{0} {1} {2}", prefix, txRef, rxRef);
    }
}
