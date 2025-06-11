// Copyright © 2025 Triamec Motion AG
using Triamec.Tam;
using Triamec.TriaLink.Adapter;

namespace TraSt_Sinus {
    internal class Program {
        static void Main(string[] args) {
            Controller controller = new Controller();       // create a new instance of the Controller class
            controller.Initialize();
            controller.StreamSinus();                       // start the streaming
        }
    }
}
