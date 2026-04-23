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
