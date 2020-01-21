﻿using System.Collections.Generic;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using System.Linq;
using Tools;
using System.Globalization;
using static Tools.RoutingController;
/// <summary>
/// 
/// </summary>

namespace DomainApp
{
    public class StateObject
    {
        // Client  socket.  
        public Socket workSocket = null;

        // Size of receive buffer.  
        public const int BufferSize = 128;

        // Receive buffer.  
        public byte[] buffer = new byte[BufferSize];

        // Received data string.  
        public StringBuilder sb = new StringBuilder();
    }
    class Program
    {
        
        public static Domain domain=new Domain();

        static void Main(string[] args)
        {
            try
            {
                domain.readinfo(args[0]);
            }
            catch(Exception e)
            {

            }
            domain.domainServer.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), domain.port));
            domain.domainServer.Listen(50);

            while(true)
            {
                domain.domainDone.Reset();
                domain.domainServer.BeginAccept(new AsyncCallback(AcceptCallBack), domain.domainServer);
                domain.domainDone.WaitOne();
            }
           
           // Thread2.Start();

        }
      
        public static void AcceptCallBack(IAsyncResult asyncResult)
        {
            domain.domainDone.Set();
            Socket listener = (Socket)asyncResult.AsyncState;
            Socket handler = listener.EndAccept(asyncResult);
            StateObject stateObject = new StateObject();
            stateObject.workSocket = handler;

            handler.BeginReceive(stateObject.buffer, 0, stateObject.buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallBack), stateObject);
            //Array.Clear(stateObject.buffer, 0, stateObject.buffer.Length);

        }
        public static void ReceiveCallBack(IAsyncResult asyncResult)
        {
            StateObject state = (StateObject)asyncResult.AsyncState;
            Socket handler = state.workSocket; //socket of client
            
            int ReadBytes;
            try
            {
                ReadBytes = handler.EndReceive(asyncResult);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, ReadBytes));
            var message = state.sb.ToString().Split(' ');
            // first message must be send to get information about connected socket: First Message <Ip address>
            if (message[0].Equals("NCC-GET")) // żądanie hosta na połączenie
            {
                String source = message[1];
                String destination = message[2];
                int speed = int.Parse(message[3]);
                Console.WriteLine("Speed" + speed);
                Console.WriteLine("Checking in directory...");
                IPAddress sourceAddress = domain.NCC.DirectoryRequest(source);
                IPAddress destAddress = domain.NCC.DirectoryRequest(destination);
                bool flag = false;
                Console.WriteLine("Checking policy...");
                flag = domain.NCC.PolicyRequest(sourceAddress, destAddress);

                if (sourceAddress != null && destAddress != null)
                {                               
                }
                else
                {
                    domain.domainClient.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), domain.secondDomainPort));
                    List<byte> buffer = new List<byte>();
                    buffer.AddRange(Encoding.ASCII.GetBytes("RC-giveDomainPoint " + sourceAddress.GetAddressBytes() + " "+ destAddress.GetAddressBytes()));
                    domain.domainClient.Send(buffer.ToArray());
                }
                if (flag)
                { 
                    Console.WriteLine("You can set connection");
                }
                if (sourceAddress != null && destAddress != null)
                { //RC w swoim pliku ma odległość przy danym source i destination więc to też do zrobienia
                   RoutingResult routingResult=domain.RC.DijkstraAlgorithm(sourceAddress, destAddress, domain.RC.cables, domain.RC.lrms, speed); // prototyp funkcji Dijkstry
                    List<int> idxOfSlots = new List<int>();
                    for(int i=0; i<10;i++)
                    {
                        if(routingResult.slots[i])
                        {
                            idxOfSlots.Add(i);
                            Console.WriteLine("Index of slot: " + i);
                        }
                    }
                    foreach(var node in routingResult.Path)
                    {
                        Console.WriteLine("Chosen node: "+node.ToString());
                    }
                    List<byte> bufferToSend = new List<byte>();
                    int ct = 0;
                    foreach (var cab in routingResult.nodeAndPorts)
                    {

                        bool flaga = false;
                        Socket socket = domain.CC.SocketfromIP[cab.Key];
                        if (!flaga && ct!=routingResult.nodeAndPorts.Count-1)
                        {
                            bufferToSend.AddRange(Encoding.ASCII.GetBytes("ACK" + BitConverter.GetBytes(idxOfSlots[0]) + BitConverter.GetBytes(idxOfSlots[idxOfSlots.Count - 1]) + BitConverter.GetBytes(cab.Value)));
                            flaga = true;
                            ++ct;
                            continue;
                        }
                        else if ( ct== routingResult.nodeAndPorts.Count - 1) // ostatni będzie host source
                        {
                            bufferToSend.AddRange(Encoding.ASCII.GetBytes("ACK" + BitConverter.GetBytes(idxOfSlots[0]) + BitConverter.GetBytes(idxOfSlots[idxOfSlots.Count - 1]) + BitConverter.GetBytes(cab.Value)));
                            socket.BeginSend(bufferToSend.ToArray(), 0, bufferToSend.ToArray().Length, 0,
                        new AsyncCallback(SendCallBack), socket);
                            bufferToSend.Clear();
                           // ct = 0;
                        }
                        else
                        {
                            bufferToSend.AddRange(BitConverter.GetBytes(cab.Value)); //inport of node
                            ++ct;
                            socket.BeginSend(bufferToSend.ToArray(), 0, bufferToSend.ToArray().Length, 0,
                        new AsyncCallback(SendCallBack), socket);
                            bufferToSend.Clear();
                            ++ct;
                        }                      
                    }
                    

                }

                //Domain.NCC.ConnectionRequest(sourceAddress, destAddress, speed);
                
            }
            if(message[0].Equals("RC-SecondDomainTopology"))
            {
                IPAddress borderAddress = IPAddress.Parse(message[1]);
              //  Domain.RC.DijkstraAlgorithm(sourceAddress, borderAddress, Domain.RC.cables, Domain.RC.lrms, numberOfSlots);
            }
            if(message[0].Equals("CC-callin"))
            {
                domain.CC.IPfromSocket.Add(handler, IPAddress.Parse(message[1]));
                domain.CC.SocketfromIP.Add(IPAddress.Parse(message[1]), handler); // router wysyła też swoje LRMy więc trzeba je dodać do RC
                Console.WriteLine("Called in to domain: " +IPAddress.Parse(message[1]));
                domain.RC.nodesToAlgorithm.Add(IPAddress.Parse(message[1]));
                List<byte> bufferLRM = new List<byte>();
                bufferLRM.AddRange(Encoding.ASCII.GetBytes(message[2]));
                for(int j=0; j<bufferLRM.Count;j++)
                {
                    Console.Write(bufferLRM[j] + " ");
                    
                }
                Console.WriteLine();
                ushort port1 = (ushort)((bufferLRM[1] << 8) + bufferLRM[0]);
                Console.WriteLine(port1);
                Console.WriteLine(bufferLRM.Count);
                byte[] buffer = new byte[16];
                int i = 0;
                while(i<bufferLRM.Count)
                {
                    buffer = bufferLRM.GetRange(i, 16).ToArray();
                    ushort port = (ushort)((buffer[1] << 8) + buffer[0]);
                    Console.WriteLine(port);
                    LinkResourceManager LRM = LinkResourceManager.returnLRM(buffer);
                    i += 16;
                    Console.WriteLine("Port: " +LRM.port);
                    domain.RC.lrms.Add(LRM);
                }
               // Array.Clear(state.buffer, 0, state.buffer.Length);

            }
            if(message[0].Equals("RC-giveDomainPoint"))
            {
                //zwróci punkt bazując na tym kto jst sourcem a kto destination
                IPAddress ipBorderNode = null;
                ushort portBorderNode=0;
                domain.domainClient.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), domain.secondDomainPort));
                List<byte> buffer = new List<byte>();
                buffer.AddRange(Encoding.ASCII.GetBytes("RC-SecondDomainTopology " + ipBorderNode.GetAddressBytes() + " " +BitConverter.GetBytes(portBorderNode)));
                domain.domainClient.Send(buffer.ToArray());
            }
            if(message[0].Equals("SUBNETWORK-callin"))
            {
                domain.CC.IPfromSocket.Add(handler, IPAddress.Parse(message[1]));
                domain.CC.SocketfromIP.Add(IPAddress.Parse(message[1]), handler);
                domain.RC.nodesToAlgorithm.Add(IPAddress.Parse(message[1]));
                Console.WriteLine("Subnetwork called in: " + IPAddress.Parse(message[1]));

            }
            state.sb.Clear();
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallBack), state);
        }
        

      

           // zwykła dijkstra, trzeba wrzucić ją w RC i napisać prawidłową
            
           



            public static Cable findCableBetweenNodes(IPAddress ip1,IPAddress ip2, List<Cable> cables)
            {
                Cable cable = null;
                for(int i=0; i<cables.Count;i++)
                {
                    if((cables[i].Node1==ip1 && cables[i].Node2==ip2) || (cables[i].Node2 == ip1 && cables[i].Node1 == ip2))
                    {
                        cable = cables[i];
                        break;
                    }
                }
                return cable;
            }
            public static LinkResourceManager findLRM(IPAddress ip, ushort port, List<LinkResourceManager> links)
            {
                LinkResourceManager link = null;
                foreach(var l in links)
                {
                    if (l.IPofNode==ip && l.port == port)
                    {
                        link = l;
                        break;
                    }
                        
                }
                return link;
            }
            private static void Send(Socket handler, String data)
            {
                byte[] byteData = Encoding.ASCII.GetBytes(data);

                handler.BeginSend(byteData, 0, byteData.Length, 0,
                    new AsyncCallback(SendCallBack), handler);
            }

            public static void SendCallBack(IAsyncResult ar)
            {
                try
                {
                    Socket handler = (Socket)ar.AsyncState;

                    int bytesSent = handler.EndSend(ar);


                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
        }
}


