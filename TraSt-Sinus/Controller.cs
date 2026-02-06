
// Copyright © 2025 Triamec Motion AG

using Triamec.Tam;
using Triamec.Tam.Motion;
using Triamec.Tam.Motion.TrajectoryStreaming;
using Triamec.TriaLink.Adapter;


namespace TraSt_Sinus {
    internal class Controller : IAsyncDisposable {
        readonly double _streamRate = 50000;                // Hz
        double _duration;                                   // seconds
        private bool disposedValue;
        readonly int _maxPointsPerSegment = 5000;

        StreamingAbortListener _abortListener = null!;
        SineSegmentGenerator _sineSegmentGenerator = null!;

        ITrajectoryStreamer _streaming = null!;
        IAxisGroup<IAxisWithRegisterLayoutId> _axisGroup = null!;
        IMotionSystem _motionSystem = null!;

        public Controller() {
        }

        public async Task Initialize() {
            _motionSystem = await MotionSystem.Connect().ConfigureAwait(false);

            int requestedAxesCount;                                                                         // User needs to configure how many axes he wants to use
            while (true) {
                Console.WriteLine("How many Axes do you want to use? (Enter a number)");
                string? input = Console.ReadLine();

                if (int.TryParse(input, out requestedAxesCount) && requestedAxesCount > 0) {               // check if valid
                    break;
                } else {
                    Console.WriteLine("Invalid input. Please enter a positive number (integer).\n");       // if not valid, print error message and continue loop
                    continue;
                }
            }

            Console.WriteLine("\nSet up TamSystem... ");
            TamTopology topology = new TamTopology();                                                       // create the root object representing the topology of the TAM hardware
            TamSystem system = topology.AddLocalSystem(DataLinkLayers.Network);                             // adds the local TAM on this PC to the topology, but looks only for the network layer
            system.Identify();                                                                              // identify the system, which will connect to the TAM hardware and discover all axes

            TamAxis[]? allFoundAxes = topology.AsDepthFirst<TamAxis>().ToArray();                           // create a list with all axes found in the topology

            if (allFoundAxes.Length < requestedAxesCount) {                                                 // check if the number of axes found is less than the number of axes requested
                Console.WriteLine($"\nOnly {allFoundAxes.Length} axes found, but You requested to control {requestedAxesCount} axes.");
                Console.WriteLine($"The application continues with {allFoundAxes.Length}");
            }


            _axisGroup = await _motionSystem.Axes.CreateGroup([.. allFoundAxes.Take(requestedAxesCount).Select(a => a.Name)]);       // create an axis group with the requested number of axes

            Console.WriteLine("\nEnabling axes...");
            await _axisGroup.Enable().ConfigureAwait(false);                                                // enable all axis in the axis group, so that they can execute commands. Wait for up to 5 seconds for the axes to be enabled.
            Console.WriteLine("Homing axes...");
            await _axisGroup.Home().ConfigureAwait(false);                                                  // home all axes in the axis group. This is required to ensure that all axes are in a known position before starting the trajectory streaming.

            while (true) {
                Console.WriteLine("\nHow long should the movement last (seconds)?");
                string? input = Console.ReadLine();

                if (double.TryParse(input, out _duration) && _duration > 0.0) {                             // check if valid
                    break;
                } else {
                    Console.WriteLine("Invalid input. Please enter a valid number (double).\n");            // if not valid, print error message and continue loop
                    continue;
                }
            }

            _streaming = await _axisGroup.PrepareTrajectoryStreaming().SetStreamRate(_streamRate).CreateStreamer(); // Prepare the trajectory streaming for the axis group with the specified stream rate
            _streaming.Override = 100f;                                                                     // speed override in percent, 100% means the speed is not overridden

            _sineSegmentGenerator = new SineSegmentGenerator(_maxPointsPerSegment, _duration, _streamRate, _axisGroup.Count);

            _abortListener = new StreamingAbortListener() {                                                 // Create a listener for aborted streaming
                Streaming = _streaming
            };
            _abortListener.Start();                                                                         // Start listening for abort key ('q' or 'ESC') presses in a separate thread.
        }

        public async Task StreamSinus() {
            Console.WriteLine("Start streaming sine segments:");

            double[,] currentSegment = _sineSegmentGenerator.GenerateSegment();

            Task<double[,]>? nextSegmentTask = null;

            if (_sineSegmentGenerator.HasMoreData()) {
                nextSegmentTask = Task.Run(()=>_sineSegmentGenerator.GenerateSegment());                    // Start generating the next segment in a separate task to avoid blocking the main thread
            }

            int segmentCounter = 0;
            while (!_abortListener.IsAborted) {                                                             // while the streaming is not aborted and duration is not passed, stream the sine segments
                Console.WriteLine();
                while (!_abortListener.IsAborted) {
                    try {
                        bool isLastSegment = (nextSegmentTask == null);
                        bool ok = _streaming.Move(currentSegment, null, isLastSegment);
                        if (ok) {
                            segmentCounter++;
                            Console.Write($"\rSegment {segmentCounter}/{_sineSegmentGenerator.NumTotalSegments} added in the streaming buffer.");
                            break;
                        } else {                                                                            // sending to streaming-buffer succeeded, proceed to next segment
                            Console.Write("\rStreaming buffer full, waiting...");
                            Thread.Sleep(10);                                                               // wait and retry if buffer is full
                        }
                    } catch (Exception ex) {                                                                // if move throws error, mark as aborted due to error and exit
                        _abortListener.AbortByError(ex);
                        break;
                    }
                }

                if (_abortListener.IsAborted || nextSegmentTask == null) { break; }                         // if aborted or no next segment task, exit the loop

                currentSegment = nextSegmentTask.Result;                                                    // wait for the next segment to be generated and get the result

                if (_sineSegmentGenerator.HasMoreData()) {                                                  // if there is more data to stream, start generating the next segment
                    nextSegmentTask = Task.Run(() =>_sineSegmentGenerator.GenerateSegment());
                } else {
                    nextSegmentTask = null;                                                                 // if no more data, set next segment task to null
                }
            }


            if (_streaming.IsStreaming && !_abortListener.IsAborted) {
                Console.WriteLine("\nWaiting for streaming to complete...");
            }

            while (_streaming.IsStreaming && !_abortListener.IsAborted) {                                    // wait until the streaming is completed or aborted
                Console.Write($"actual: {_streaming.ActualIndex}, streamed: {_streaming.StreamedIndex}\n");
                Thread.Sleep(1000);
            }

            if (!_abortListener.IsAborted) {
                Console.WriteLine("\n\nStreaming completed successfully.");
            }

            Console.WriteLine("\nDisabling axis...");
            await _axisGroup.Disable().ConfigureAwait(false);

            _abortListener.ListenToExit = true;                                                             // change abortKey Listener to exitKey-Listener
            while (true) {
                Thread.Sleep(100);
                if (_abortListener.Exit == true) {
                    break;
                }
            }
        }

        public async ValueTask DisposeAsync() {
            if (!disposedValue) {
                if (_streaming != null) {
                    await _streaming.DisposeAsync().ConfigureAwait(false);
                }

                if (_axisGroup != null) {
                    await _axisGroup.Disable().ConfigureAwait(false);
                }

                if (_motionSystem != null) {
                    await _motionSystem.DisposeAsync().ConfigureAwait(false);
                }

                disposedValue = true;
            }
        }
    }
}
