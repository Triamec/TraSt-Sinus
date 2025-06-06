
// Copyright © 2025 Triamec Motion AG

using Triamec.Tam;
using Triamec.TriaLink;
using Triamec.TriaLink.Adapter;


namespace TraSt_Sinus {
    internal class Controller {
        readonly TrajectoryStreamingAxisGroups _axisGroup;
        readonly double _streamRate = 50000;                             // Hz
        TrajectoryStreaming _streaming;
        StreamingAbortListener _abortListener;
        SineSegmentGenerator _sineSegmentGenerator;
        double _duration;
        readonly int _maxPointsPerSegment = 100000;

        public Controller() {
            _axisGroup = new TrajectoryStreamingAxisGroups("MyAxisGroup");
        }

        public void Initialize() {
            int requestedAxesCount;                                                                         // User needs to configure how many axes he wants to use
            while (true) {
                Console.WriteLine("How many Axes do you want to use? (Enter a number)");
                string? input = Console.ReadLine();

                if (int.TryParse(input, out requestedAxesCount) && requestedAxesCount > 0) {                // check if valid
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

            for (int i = 0; i < requestedAxesCount && i < allFoundAxes.Length; i++) {                       // iterate through the found axes, but only to the number of axes requested or the number of axes found, whichever is smaller
                Console.WriteLine($"...connecting to {allFoundAxes[i].Name} of station {allFoundAxes[i].Drive.Station.Name} and add to axisGroup.");
                allFoundAxes[i].Drive.ResetFault();
                _axisGroup.AddAxis(new TraStAxis(allFoundAxes[i]));                                         // create a new TraStAxis instance based on the found axis and add it to the axis group                                            
            }


            //TODO: Is OverrideControlSystem really required? And if yes, maybe possible inside axisGroup?
            foreach (TraStAxis axis in _axisGroup.Axes) {                                                   // tell all axis in axis group, that we're going to take control. Otherwise, the axis might reject our commands
                axis.SetOverrideControlSystem(true);
            }

            _axisGroup.Enable().WaitForSuccess(TimeSpan.FromSeconds(5));                                    // enable all axis in the axis group, so that they can execute commands. Wait for up to 5 seconds for the axes to be enabled.
            _axisGroup.Home();                                                                              // home all axes in the axis group. This is required to ensure that all axes are in a known position before starting the trajectory streaming.

            while (true) {
                Console.WriteLine("\nHow long should the movement last (seconds)?");
                string? input = Console.ReadLine();

                if (double.TryParse(input, out _duration)) {                                            // check if valid
                    break;
                } else {
                    Console.WriteLine("Invalid input. Please enter a valid number (double).\n");        // if not valid, print error message and continue loop
                    continue;
                }
            }

            _streaming = new TrajectoryStreaming {
                AxisGroup = _axisGroup,                                                                     // define which group of axes should execute the streamed motion commands
                StreamRate = (uint)_streamRate,                                                             // interval between streamed points in microseconds
                Override = 100f,                                                                            // speed override in percent, 100% means the speed is not overridden
            };

            _sineSegmentGenerator = new SineSegmentGenerator(_maxPointsPerSegment, _duration, _streamRate, _axisGroup.Axes.Count);

            _abortListener = new StreamingAbortListener() {                                                 // Create a listener for aborted streaming
                Streaming = _streaming
            };
            _abortListener.Start();                                                                         // Start listening for abort key ('q' or 'ESC') presses in a separate thread.
        }

        public void StreamSinus() {
            Console.WriteLine("Start streaming sine segments:");

            double[,] currentSegment = _sineSegmentGenerator.GenerateSegment();

            Task<double[,]>? nextSegmentTask = null;

            if (_sineSegmentGenerator.HasMoreData()) {
                nextSegmentTask = Task.Run(() => { return _sineSegmentGenerator.GenerateSegment(); });                        // Start generating the next segment in a separate task to avoid blocking the main thread
            }

            while (!_abortListener.IsAborted) {                                                             // while the streaming is not aborted and duration is not passed, stream the sine segments

                while (!_abortListener.IsAborted) {
                    try {
                        bool isLastSegment = (nextSegmentTask == null);
                        bool ok = _streaming.Move(currentSegment, null, isLastSegment);
                        if (ok) {
                            Console.WriteLine("Segment added to streaming buffer.");
                            break;
                        } else {                                                                            // sending to streaming-buffer succeeded, proceed to next segment
                            Console.WriteLine("Streaming buffer full, waiting...");
                            Thread.Sleep(30);                                                               // wait and retry if buffer is full
                        }
                    } catch (Exception ex) {                                                                // if move throws error, mark as aborted due to error and exit
                        _abortListener.AbortByError(ex);
                        break;
                    }
                }

                if (_abortListener.IsAborted || nextSegmentTask == null) { break; }                                                      // if aborted or no next segment task, exit the loop

                currentSegment = nextSegmentTask.Result;                                                      // wait for the next segment to be generated and get the result

                if (_sineSegmentGenerator.HasMoreData()) {                                                      // if there is more data to stream, start generating the next segment
                    nextSegmentTask = Task.Run(() => { return _sineSegmentGenerator.GenerateSegment(); });
                } else {
                    nextSegmentTask = null;                                                                 // if no more data, set next segment task to null
                }
            }


            if (_streaming.IsStreaming && !_abortListener.IsAborted) {
                Console.WriteLine("\nWaiting for streaming to complete...");
            }

            while (_streaming.IsStreaming && !_abortListener.IsAborted) {                                    // wait until the streaming is completed or aborted
                Console.Write(".");
                Thread.Sleep(500);
            }

            if (!_abortListener.IsAborted) {
                Console.WriteLine("\n\nStreaming completed successfully.");
            }

            Console.WriteLine("\nDisabling axis...");
            _axisGroup.Disable().WaitForSuccess(TimeSpan.FromSeconds(5));                                   // disable the axis group after streaming is completed

            _abortListener.ListenToExit = true;                                                             // change abortKey Listener to exitKey-Listener
            while (true) {
                if (_abortListener.Exit == true) {
                    break;
                }
            }
        }

    }
}
