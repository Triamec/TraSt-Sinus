// Copyright © 2025 Triamec Motion AG
using Triamec.Tam;
using Triamec.Tam.Registers.Tags;
using Triamec.Tam.Registers;
using Triamec.Tam.Requests;
using Triamec.Tam.Rlid19;
using Triamec.TriaLink;
using AxisRegister = Triamec.Tam.Rlid19.Axis;
using TimeoutException = Triamec.Tam.TimeoutException;

namespace TraSt_Sinus {
    public class TraStAxis : ITrajectoryStreamingAxis {
        private readonly TamAxis _axis;
        private readonly AxisRegister _register;

        public event EventHandler? AxisError;
        public event EventHandler<StateTransition>? Transition;
        private event EventHandler? HomingStateChanged;

        // Flag to track if we added the observer ourselves
        private bool _observerAdded;

        public TraStAxis(TamAxis axis) {
            _axis = axis ?? throw new ArgumentNullException(nameof(axis));
            _register = (AxisRegister)_axis.Register;

            // Add state observer if not already added by someone else
            if (axis.Drive.StateObserverCount < 3) {
                axis.Drive.AddStateObserver(this);
                _observerAdded = true;
                _axis.Transition += OnTransition;
            }
        }

        public bool UseVelocityExtension {
            get => _register.Commands.PathPlanner.Stream.ExtensionMode.UseVelocity.Read();
            set => _register.Commands.PathPlanner.Stream.ExtensionMode.UseVelocity.Write(value);
        }

        public bool UseCurrentExtension {
            get => _register.Commands.PathPlanner.Stream.ExtensionMode.UseCurrent.Read();
            set => _register.Commands.PathPlanner.Stream.ExtensionMode.UseCurrent.Write(value);
        }

        public uint AdditionalExtension {
            get => _register.Commands.PathPlanner.Stream.ExtensionMode.RowSize.Read();
            set => _register.Commands.PathPlanner.Stream.ExtensionMode.RowSize.Write(value);
        }

        public bool Continuous {
            get => _register.Commands.PathPlanner.Stream.FeedOverride.Continuous.Read();
            set => _register.Commands.PathPlanner.Stream.FeedOverride.Continuous.Write(value);
        }

        public float Timeout {
            get => _register.Commands.PathPlanner.Stream.Error.FeedTimeout.Read();
            set => _register.Commands.PathPlanner.Stream.Error.FeedTimeout.Write(value);
        }

        public double SafePosition { get; set; }

        public bool SourceStreaming {
            get => _register.Commands.PathPlanner.Stream.Source.Read() == StreamSource.Streaming;
            set => _register.Commands.PathPlanner.Stream.Source.Write(value ? StreamSource.Streaming : StreamSource.Tama);
        }

        public int StreamingTableSize {
            get => _register.Commands.PathPlanner.Stream.TableSize.Read();
            set => _register.Commands.PathPlanner.Stream.TableSize.Write(value);
        }

        public int StreamingTableEnd {
            get => _register.Commands.PathPlanner.Stream.TableEnd.Read();
            set => _register.Commands.PathPlanner.Stream.TableEnd.Write(value);
        }

        public bool IsStreaming => _register.Signals.PathPlanner.Stream.State.Read() > 0;

        public Timestamp ActualTimestamp => Timestamp.FromTamValue32(new TamValue32(_axis.FindReadonlyRegister<uint>(GeneralSignalTags.Timestamp).Read()));

        public void SetOverrideControlSystem(bool value) {
            _axis.ControlSystemTreatment.Override(value);
        }

        // Implementiere die IAxis-Methoden analog zu deinem System
        public TamRequest Enable() => _axis.Control(AxisControlCommands.ResetErrorAndEnable);
        public TamRequest Disable() => _axis.Control(AxisControlCommands.Disable);
        public TamRequest Reset() => _axis.Control(AxisControlCommands.ResetError);
        public TamRequest MoveAbsolute(double position) => _axis.MoveAbsolute(position);
        public TamRequest MoveRelative(double distance) => _axis.MoveRelative(distance);
        public TamRequest MoveVelocity(double velocity) => _axis.MoveVelocity((float)velocity);
        public TamRequest Stop() => _axis.Stop();
        public TamRequest Stop(bool emergency) => _axis.Stop();
        public TamRequest Stop(float deceleration) => _axis.Stop(deceleration);
        public TamRequest Halt() => _axis.Halt();
        public TamRequest Halt(float deceleration) => _axis.Halt(deceleration);
        public bool Home() {
            // Check if axis is in a state that allows homing
            if (State != AxisState.Standstill && State != AxisState.Disabled) {
                return false;
            }

            try {
                // Access the axis registers directly
                var reg = (AxisRegister)_axis.Register;

                // Check if homing is configured
                var homingMethod = reg.Parameters.Homing.Method.Read();
                if (homingMethod == HomingMethod.None) {
                    return false;
                }

                // Start monitoring homing state changes
                var pollingInterval = TimeSpan.FromMilliseconds(100);
                var timeout = TimeSpan.FromSeconds(10);
                return StartAndMonitorHomingStateChanges(pollingInterval, timeout).Result;
            } catch {
                // Handle any exceptions during homing command
                return false;
            }
        }

        // Method to monitor homing state changes with timeout

        private async Task<bool> StartAndMonitorHomingStateChanges(TimeSpan pollingInterval, TimeSpan timeout) {
            var reg = (AxisRegister)_axis.Register;
            reg.Commands.Homing.Command.Write(HomingCommand.Invalidate);
            var previousState = reg.Signals.Homing.State.Read();
            var cancellationTokenSource = new CancellationTokenSource(timeout);



            // Send the homing command
            reg.Commands.Homing.Command.Write(HomingCommand.Start);

            try {
                while (true) {
                    await Task.Delay(pollingInterval, cancellationTokenSource.Token); // Polling interval with timeout

                    var currentState = reg.Signals.Homing.State.Read();
                    if (currentState != previousState) {
                        previousState = currentState;
                        HomingStateChanged?.Invoke(this, EventArgs.Empty);

                        if (currentState == HomingState.HomingDone) {
                            return true; // Homing completed successfully
                        } else if (currentState.ToString().StartsWith("Error", StringComparison.Ordinal)) {
                            return false; // Homing failed
                        }
                    }
                }
            } catch (TaskCanceledException) {
                throw new TimeoutException("Monitoring homing state changes timed out.");
            }
        }

        public bool IsEnabled {
            get {
                bool result;
                switch (State) {
                    case AxisState.Standstill:
                    case AxisState.DiscreteMotion:
                    case AxisState.ContinuousMotion:
                    case AxisState.DirectCoupledMotion:
                    case AxisState.TamaCoupledMotion:
                    case AxisState.Stopping:
                    case AxisState.ErrorStopping:
                        result = true;
                        break;

                    default:
                        result = false;
                        break;
                }
                return result;
            }
        }

        public bool IsHomed {
            get {
                var reg = (AxisRegister)_axis.Register;
                var state = reg.Signals.Homing.State.Read();
                var referenceDone = reg.Commands.PositionController.ReferenceDone.Read();
                return state == HomingState.HomingDone && referenceDone;
            }
        }
        public bool IsStandstill => State == AxisState.Standstill;
        public double ActualPosition => throw new NotImplementedException();
        public float ActualVelocity => throw new NotImplementedException();
        public float ActualAcceleration => throw new NotImplementedException();
        public float ActualJerk => throw new NotImplementedException();
        public float ErrorPosition => throw new NotImplementedException();
        public float MaximumAcceleration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float MaximumDeceleration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float EmergencyDeceleration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float MaximumJerk { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float EmergencyJerk { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float MaximumVelocity { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float PositionMaximum { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float PositionMinimum { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float ModuloPositionMaximum { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float ModuloPositionMinimum { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string PositionUnit { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        //ToDo: implement
        public AxisErrorIdentification Error{get => AxisErrorIdentification.None; set => Console.WriteLine("HelloSet"); }
        public AxisState State => _axis.ReadAxisState();
        public int AxisIndex => _axis.AxisIndex;
        public string AxisName => _axis.Name;

        public RegisterComposite Register => _axis.Register;

        public TamStation Station => _axis.Drive.Station;

        public void OnAxisError(object sender, EventArgs e) => AxisError?.Invoke(sender, e);

        public void OnTransition(object? sender, StateTransition e) {
            if (sender is ITamDrive drive) {
                // Check for error state
                if (e.Errors != MergedErrors.NO_ERROR) {
                    // Trigger the axis error event
                    OnAxisError(this, EventArgs.Empty);
                }
                // Forward the transition event to subscribers
                Transition?.Invoke(this, e);
            }
        }
    }
}
