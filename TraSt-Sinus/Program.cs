using Triamec.Tam;
using Triamec.Tam.Periphery;
using Triamec.TriaLink.Adapter;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TraSt_Sinus {
    internal class Program {



        static void Main(string[] args) {

            #region Configuration

            int _numberOfAxes;               // number of axes the application can work with
            List<TraStAxis> _axes;           // list of axes the application can work with
            double _duration;
            double _streamRate = 50000;      // Hz


            // User needs to configure how many axes he wants to use. The input needs to be a 
            // valid integer, else the loop continues until a valid input is given. 
            while (true) {
                Console.WriteLine("How many Axes do you want to use? (Enter a number)");
                string? input = Console.ReadLine();

                if (int.TryParse(input, out _numberOfAxes)) {                                           // check if valid
                    break;
                } else {
                    Console.WriteLine("Invalid input. Please enter a valid number (integer).\n");       // if not valid, print error message and continue loop
                    continue;
                }
            }
            _axes = ConnectAxes(_numberOfAxes);                                                         // Get a list with connected TraStAxis instances based on the number of axes the user


            // Set up the axis group for trajectory streaming. 
            // Includes creating, taking control over the axes, enabling them, and homing them.
            var axisGroup = new TrajectoryStreamingAxisGroups("MyAxisGroup");                           // create a new axis group for the trajectory streaming
            foreach (var axis in _axes) {                                                               // Add all axes from the list to the axis group
                axisGroup.AddAxis(axis);
            }
            //TODO: Is OverrideControlSystem really required? And if yes, is it possible over axisGroup?
            foreach (var axis in _axes) {
                axis.SetOverrideControlSystem(true);
            }                                                                                           // Tell all axis in axis group, that we're going to take control. Otherwise, the axis might reject our commands.
            axisGroup.Enable().WaitForSuccess(TimeSpan.FromSeconds(5));                                 // Enable all axis in the axis group, so that they can execute commands. Wait for up to 5 seconds for the axes to be enabled.
            axisGroup.Home();                                                                           // Home all axes in the axis group. This is required to ensure that all axes are in a known position before starting the trajectory streaming.


            // Create a trajectory streaming configuration object with the desired parameters.
            var streaming = new TrajectoryStreaming {
                AxisGroup = axisGroup,                                                                  // define which group of axes should execute the streamed motion commands
                StreamRate = (uint)_streamRate,                                                         // intervall between streamed points in microseconds
                Override = 100f,                                                                        // speed override in percent, 100% means the speed is not overridden
                ContinousMode = false,
                Redundance = true
            };


            // User needs to configure how long the movement should last. The input needs to
            // be a valid double, else the loop continues until a valid input is given.
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
            #endregion


            #region CreatingAndStartStreaming

            // Prepare for calculation and segmentation of trajectory points
            int numTotalPoints = (int)(_duration * _streamRate);                                                    // total number of trajectory points to be streamed
            int maxNumPointsPerSegment = 5000;                                                                      // maximal number of trajectory points in one segment
            int numSegments = (int)Math.Ceiling((double)numTotalPoints / maxNumPointsPerSegment);                   // number of segments
            double timeBetweenPoints = 1.0 / _streamRate;                                                           // time interval between consecutive points in seconds                                              
            bool abort = false;                                                                                     // flag to abort the streaming process if needed
            Console.WriteLine($"\nStarting trajectory streaming with {numTotalPoints} points, divided into {numSegments} segments of up to {maxNumPointsPerSegment} points each.");


            // Generate and stream trajectory segments to the drive: 
            // Each segment contains precomputed position values (sinusoidal) for all axes
            // Segments are sent sequentially to the streaming buffer, ensuring reliable transmissions.
            for (int segment = 0; (segment < numSegments) && (!abort); segment++) {

                int segmentStartPointIndex = segment * maxNumPointsPerSegment;                                      // Index of the first trajectory point in the current segment ( = p[segmentStartPoint, X])
                int pointsInSegment = Math.Min(maxNumPointsPerSegment, numTotalPoints - segmentStartPointIndex);    // How many points are in this segment? If the segment is the last one, it might have fewer points than maxNumPointsPerSegment.
                double[,] positions = new double[pointsInSegment, axisGroup.Axes.Count];                            // Initialize a 2D array to store the values (positions) for all trajectory points in this segment (rows = trajectory points; columns = axes in axis group)


                for (int i = 0; i < pointsInSegment; i++) {                                                         // Loop through each trajectory point in the current segment to calculate its value (position)

                    int globalPointIndex = segmentStartPointIndex + i;                                              // Calculate the global index of the point (relative to the whole trajectory)
                    double t = globalPointIndex * timeBetweenPoints;                                                // Calculate the time corresponding to the actual point
                    double value = Math.Sin(2 * Math.PI * t);                                                       // Calculate the sinusoidal position value at time t


                    for (int j = 0; j < axisGroup.Axes.Count; j++) {                                                // Assign the calculated value to each trajectory point for all axes in the axis group
                        positions[i, j] = value;
                    }
                }

                bool isLastSegment = (segment == numSegments - 1);                                                  // Check if this is the last segment


                // A complete segment is now ready to be streamed to the drive.
                // Attempt to add it to the streaming buffer. If the buffer is full, wait and retry.
                // If an error occurs during streaming, abort the process.
                while (true) {
                    try {
                        bool ok = streaming.Move(positions, null, isLastSegment);                                   // Add to streaming buffer
                        if (ok) { break; }                                                                          // Exit the loop if the move was successful
                        else {
                            Console.WriteLine("Buffer full, waiting...");
                            Thread.Sleep(10);
                        }
                    } catch (Exception ex)                                                                            // catch any exception that occurs during the streaming process
                      {
                        Console.WriteLine($"Error during streaming.Move() {ex.Message}");
                        abort = true; // Set abort flag to true if an error occurs                                  // set abort flat
                        break;
                    }
                }


                // Update and display a progress bar 
                // showing ho many segments have been added to the streaming buffer.
                int barLength = 40;
                int progress = (int)((double)(segment + 1) / numSegments * barLength);                              // Calculate the progress based on the current segment
                string bar = new string('#', progress).PadRight(barLength, '-');
                Console.Write($"\r[{bar}] {segment + 1}/{numSegments} segments added to the streaming buffer.");
            }
            #endregion


            #region WaitingForStreamingCompletion

            // After all segments have been added to the streaming buffer, 
            // wait until the streaming buffer has fully sent the trajectory data to the drive. 
            // Once streaming is complete or aborted, disable the axis group and display the result.
            if (streaming.IsStreaming) {
                Console.WriteLine("\n\nWaiting for the streaming buffer to finish sending all segments to the drive:");
            }

            while (streaming.IsStreaming && !abort) {                                           // Wait until all trajectory data has been sent or until an abort occurs
                Console.Write(".");                                                             // visual indication of waiting
                Thread.Sleep(500);
            }

            axisGroup.Disable();                                                                // Disable the axis group to stop any further commands
            if (!abort) {
                Console.WriteLine("\n\nMovement completed successfully.");
            } else {
                axisGroup.Disable();
                Console.WriteLine("\n\nMovement aborted because an error occurred.");
            }
        }
        #endregion


        /// <summary>
        ///  Connects to the available axes in the TAM topology and returns a list of TraStAxis instances 
        ///  up to the number by the user.
        ///  
        /// This method initializes the TAM topology, identifies connected hardware over the network, 
        /// and collects available axes using a depth-first search approach. If fewer axes are found than requested
        /// a warning is printed and only the found axes are returned.
        /// 
        /// </summary>
        /// <param name="numberOfAxes"> The number of axes the application intends to control</param>
        /// <returns>A list of connected TraStAxis objects</returns>
        static List<TraStAxis> ConnectAxes(int numberOfAxes) {
            TamTopology topology = new TamTopology();                                       // create the root object representing the topology of the TAM hardware
            TamSystem system = topology.AddLocalSystem(DataLinkLayers.Network);             // adds the local TAM on this PC to the topology
            system.Identify();                                                              // boot the Tria-Link so that it learns about connected stations

            //TODO Kann ich hier auch Tam configurations auf Drives laden?

            TamAxis[]? allFoundAxes = topology.AsDepthFirst<TamAxis>().ToArray();           // create a list with all axes found in the topology

            if (allFoundAxes.Length < numberOfAxes) {                                       // check if the number of axes found is less than the number of axes requested
                Console.WriteLine($"\nOnly {allFoundAxes.Length} axes found, but You requested to control {numberOfAxes} axes.");
                Console.WriteLine($"The application continues with {allFoundAxes.Length}");
            }

            List<TraStAxis> axes = new List<TraStAxis>();                                   // initialize an empty list to store the axes the application will work with

            Console.WriteLine();                                                            // for readability

            for (int i = 0; i < numberOfAxes && i < allFoundAxes.Length; i++) {             // iterate through the found axes, but only to the number of axes requested or the number of axes found, whichever is smaller
                Console.WriteLine($"...Connecting to {allFoundAxes[i].Name} of station {allFoundAxes[i].Drive.Station.Name}.");
                axes.Add(new TraStAxis(allFoundAxes[i]));                                   // create a new TraStAxis instance based on the found axis and add it to the list                                            
            }

            return axes;                                                                    // return the created list of TraStAxis
        }
    }
}
