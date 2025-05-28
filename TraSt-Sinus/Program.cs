using Triamec.Tam;
using Triamec.Tam.Periphery;
using Triamec.TriaLink.Adapter;

namespace TraSt_Sinus {
    internal class Program {



        static void Main(string[] args) {

            #region Configuration
            int numberOfAxes;               // number of axes the application can work with
            List<TraStAxis> axes;           // list of axes the application can work with
            double duration;
            double streamRate = 50000;      // Hz (50 kHz)



            while (true) {                                                                          // User needs to configure how many Axes he wants to use. The input needs to
                Console.WriteLine("How many Axes do you want to use? (Enter a number)");            //  be a valid integer, else the loop continues until a valid input is given.
                string? input = Console.ReadLine();

                if (int.TryParse(input, out numberOfAxes)) {                                        // check if valid
                    break;
                } else {
                    Console.WriteLine("Invalid input. Please enter a valid number (integer).\n");   // if not valid, print error message and continue loop
                    continue;
                }
            }
            // Get a list with connected TraStAxis instances based on the number of axes the user
            axes = ConnectAxes(numberOfAxes);                                                       // wants to use or maximum number of axes found.

            var axisGroup = new TrajectoryStreamingAxisGroups("MyAxisGroup");                       // create a new axis group for the trajectory streaming
            foreach (var axis in axes) {                                                            // Add all axes from the list to the axis group for synchronized streaming
                axisGroup.AddAxis(axis);
            }

            //TODO: Is OverrideControlSystem really required? And if yes, is it possible over axisGroup?
            foreach (var axis in axes) {
                axis.SetOverrideControlSystem(true);
            }                                                           // Tell all axis in axis group, that we're going to take control. Otherwise, the axis might reject our commands.
            axisGroup.Enable().WaitForSuccess(TimeSpan.FromSeconds(5));                             // Enable all axis in the axis group, so that they can execute commands. Wait for up to 5 seconds for the axes to be enabled.
            axisGroup.Home();                                                                       // Home all axes in the axis group. This is required to ensure that all axes are in a known position before starting the trajectory streaming.


            var streaming = new TrajectoryStreaming {                                               // initialize the trajectory streaming configuration with desired parameters
                AxisGroup = axisGroup,                                                              // define which group of axes should execute the streamed motion commands
                StreamRate = (uint)streamRate,                                                      // intervall between streamed points in microseconds
                Override = 100f,                                                                    // speed override in percent, 100% means the speed is not overridden
                ContinousMode = false,
                Redundance = true,
            };


            while (true) {                                                                          // User needs to configure how long the movement should last. The input needs to
                Console.WriteLine("How long should the movement last (seconds)?");                  //  be a valid integer, else the loop continues until a valid input is given.
                string? input = Console.ReadLine();

                if (double.TryParse(input, out duration)) {                                          // check if valid
                    break;
                } else {
                    Console.WriteLine("Invalid input. Please enter a valid number (double).\n");    // if not valid, print error message and continue loop
                    continue;
                }
            }

            int numTotalPoints = (int)(duration * streamRate);                                          // total number of trajectory points to be streamed
            int maxNumPointsPerSegment = 5000;                                                          // maximal number of trajectory points in one segment
            int numSegments = (int)Math.Ceiling((double)numTotalPoints / maxNumPointsPerSegment);       // number of segments
            double timeBetweenPoints = 1.0 / streamRate;                                                // time interval between consecutive points in seconds                                              
            bool abort = false;                                                                         // flag to abort the streaming process if needed


            // Alle Werte der Trajectory Points werden hier segmentweise berechnet und dann gesendet. 
            // Dabei findet eine Überprüfung statt und es wird sichergestellt, dass jedes Segment empfangen wird bevor das nächste gesendet wird.
            for (int segment = 0; (segment < numSegments) && (!abort); segment++) {

                // Index of the first trajectory point in the current segment ( = p[segmentStartPoint, X])
                int segmentStartPointIndex = segment * maxNumPointsPerSegment;

                // How many points are in this segment? If the segment is the last one, it might have fewer points than maxNumPointsPerSegment.
                int pointsInSegment = Math.Min(maxNumPointsPerSegment, numTotalPoints - segmentStartPointIndex); 

                // Initialize a 2D array to store the values (positions) for all trajectory points in this segment
                // Rows: points in segment
                // Columns: axes in axis group
                double[,] positions = new double[pointsInSegment, axisGroup.Axes.Count];

                // Loop through each trajectory point in the current segment to calculate its value (position)
                for (int i = 0; i < pointsInSegment; i++) {

                    // Calculate the global index of the point (relative to the whole trajectory)
                    int globalPointIndex = segmentStartPointIndex + i;

                    // Calculate the time corresponding to the actual point
                    double t = globalPointIndex * timeBetweenPoints;

                    // Calculate the sinusoidal position value at time t
                    double value = Math.Sin(2 * Math.PI * t);

                    // Assign the calculated value to each trajectory point for all axes in the axis group
                    for (int j = 0; j < axisGroup.Axes.Count; j++) {
                        positions[i, j] = value;
                    }
                }

                bool isLastSegment = (segment == numSegments - 1); // Check if this is the last segment

                while (true) {
                    try {
                        bool ok = streaming.Move(positions, null, isLastSegment);
                        if(ok) { break; } // Exit the loop if the move was successful
                        else {
                            Console.WriteLine("Buffer full, waiting...");
                            Thread.Sleep(10);
                        }
                    }
                    catch (Exception ex)
                    { 
                        Console.WriteLine($"Error during streaming.Move() {ex.Message}");
                        abort = true; // Set abort flag to true if an error occurs
                        break;
                    }
                }
                // TODO: Send the positions to the hardware
            }


            #endregion
        }

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

            Console.WriteLine("\n");                                                        // print a blank line for better readability in the console output

            List<TraStAxis> axes = new List<TraStAxis>();                                   // initialize an empty list to store the axes the application will work with
                                                                                            // 
            for (int i = 0; i < numberOfAxes && i < allFoundAxes.Length; i++) {             // iterate through the found axes, but only to the number of axes requested or the number of axes found, whichever is smaller
                Console.WriteLine($"...Connecting to {allFoundAxes[i].Name} of station {allFoundAxes[i].Drive.Station.Name}.");
                axes.Add(new TraStAxis(allFoundAxes[i]));                                   // create a new TraStAxis instance based on the found axis and add it to the list 
                axes[i].Station.Name = allFoundAxes[i].Drive.Station.Name;                  // set the station name explicitly (required for later reference) 
                //TODO -> is it really required?                                             
            }

            return axes;                                                                    // return the created list of TraStAxis
        }

        static void Disable() {
            throw new NotImplementedException();
        }

    }
}
