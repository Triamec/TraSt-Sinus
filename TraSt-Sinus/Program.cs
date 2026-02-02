// Copyright © 2025 Triamec Motion AG
namespace TraSt_Sinus {
    internal class Program {
        static async Task Main(string[] args) {
            var controller = new Controller();              // create a new instance of the Controller class
            await controller.Initialize();                  // initialize the streaming controller
            await controller.StreamSinus();                 // start the streaming
            await controller.DisposeAsync();                // dispose the controller and free resources
        }
    }
}
