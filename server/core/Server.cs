﻿/*
 * This file is subject to the terms and conditions defined in
 * file 'LICENSE', which is part of this source code package.
 * Copyright (c) 2016 Neproify
*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using Lidgren.Network;
using Shared;
using SharpDX;

namespace ivmp_server_core
{
    public static class Server
    {
        public static int Port;
        public static int TickRate;
        public static int MaxPlayers;

        public static NetServer NetServer;
        public static PlayersController PlayersController;
        public static VehiclesController VehiclesController;
        public static Shared.Scripting.ResourcesManager ResourcesManager;
        public static Shared.Scripting.EventsManager EventsManager;

        public static Jint.Engine Engine;

        public static Player TestPlayer;

        public static void Initialize()
        {
            TickRate = Shared.Settings.TickRate;
            if (!System.IO.File.Exists("serverconfig.xml"))
            {
                Console.WriteLine("Config file not found...");
                System.Threading.Thread.Sleep(5000);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            XmlDocument Config = new XmlDocument();
            Config.Load("serverconfig.xml");
            Port = int.Parse(Config.DocumentElement.SelectSingleNode("/config/serverport").InnerText);
            MaxPlayers = int.Parse(Config.DocumentElement.SelectSingleNode("/config/maxplayers").InnerText);
            XmlNodeList Resources = Config.DocumentElement.SelectNodes("/config/resource");

            NetPeerConfiguration NetConfig = new NetPeerConfiguration("ivmp");
            NetConfig.MaximumConnections = MaxPlayers;
            NetConfig.Port = Port;
            NetConfig.ConnectionTimeout = 50;
            NetConfig.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            NetConfig.EnableMessageType(NetIncomingMessageType.StatusChanged);
            NetServer = new NetServer(NetConfig);
            NetServer.Start();
            PlayersController = new PlayersController();
            VehiclesController = new VehiclesController();
            ResourcesManager = new Shared.Scripting.ResourcesManager();
            EventsManager = new Shared.Scripting.EventsManager();
            Engine = new Jint.Engine();

            // load resources
            foreach (XmlNode Resource in Resources)
            {
                try
                {
                    ResourcesManager.Load(Resource.Attributes["name"].InnerText);
                    ResourcesManager.Start(Resource.Attributes["name"].InnerText);
                }
                catch(Exception)
                {
                }
            }

            Timer tick = new Timer();
            tick.Elapsed += OnTick;
            tick.Interval = TickRate;
            tick.Enabled = true;
            tick.Start();
            Console.WriteLine("Started game server on Port " + Port);
            Console.WriteLine("Max Players: " + MaxPlayers);
        }

        public static void CreateTestPlayer()
        {
            if (TestPlayer == null)
            {
                TestPlayer = new Player();
                TestPlayer.Name = "TestPlayer";
                PlayersController.Add(TestPlayer);

                Console.WriteLine("Created test player. ID: " + TestPlayer.ID);
            }
        }

        public static void RemoveTestPlayer()
        {
            if (TestPlayer != null)
            {
                NetOutgoingMessage OutMsg = NetServer.CreateMessage();
                OutMsg.Write((int)Shared.NetworkMessageType.PlayerDisconnected);
                OutMsg.Write(TestPlayer.ID);
                NetServer.SendToAll(OutMsg, null, NetDeliveryMethod.ReliableSequenced, 1);

                PlayersController.Remove(TestPlayer);
                TestPlayer = null;

                Console.WriteLine("Removed test player.");
            }
        }

        public static void OnTick(object sender, ElapsedEventArgs e)
        {
            NetIncomingMessage Msg;
            while ((Msg = NetServer.ReadMessage()) != null)
            {
                switch (Msg.MessageType)
                {
                    case NetIncomingMessageType.ConnectionApproval:
                        int Version = Msg.ReadInt32();
                        if (Version == Shared.Settings.NetworkVersion)
                        {
                            Msg.SenderConnection.Approve();
                        }
                        else
                        {
                            Msg.SenderConnection.Deny();
                        }
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        NetConnectionStatus status = (NetConnectionStatus)Msg.ReadByte();
                        switch (status)
                        {
                            case NetConnectionStatus.Connected:
                                {
                                    Player Player = new Player();
                                    Player.NetConnection = Msg.SenderConnection;
                                    PlayersController.Add(Player);
                                    NetOutgoingMessage OutMsg = NetServer.CreateMessage();
                                    OutMsg.Write((int)Shared.NetworkMessageType.PlayerConnected);
                                    OutMsg.Write(Player.ID);
                                    NetServer.SendToAll(OutMsg, Msg.SenderConnection, NetDeliveryMethod.ReliableUnordered, 1);
                                    EventsManager.GetEvent("OnPlayerConnected").Trigger(Jint.Native.JsValue.FromObject(Engine, new Scripting.Natives.Player(Player)));
                                    foreach(var Resource in ResourcesManager.Resources)
                                    {
                                        Resource.SendToClient(Player.NetConnection);
                                    }
                                    break;
                                }
                            case NetConnectionStatus.Disconnected:
                                {
                                    Player Player = PlayersController.GetByNetConnection(Msg.SenderConnection);
                                    PlayersController.Remove(Player);
                                    NetOutgoingMessage OutMsg = NetServer.CreateMessage();
                                    OutMsg.Write((int)Shared.NetworkMessageType.PlayerDisconnected);
                                    OutMsg.Write(Player.ID);
                                    NetServer.SendToAll(OutMsg, Msg.SenderConnection, NetDeliveryMethod.ReliableSequenced, 1);
                                    EventsManager.GetEvent("OnPlayerDisconnected").Trigger(Jint.Native.JsValue.FromObject(Engine, new Scripting.Natives.Player(Player)));
                                    Player = null;
                                    break;
                                }
                        }
                        break;
                    case NetIncomingMessageType.DebugMessage:
                        break;
                    case NetIncomingMessageType.WarningMessage:
                        break;
                    case NetIncomingMessageType.Data:
                        int MsgType = Msg.ReadInt32();
                        switch (MsgType)
                        {
                            case (int)Shared.NetworkMessageType.UpdatePlayer:
                                PlayerUpdateStruct PlayerData = new PlayerUpdateStruct();
                                Msg.ReadAllFields(PlayerData);
                                Player Player = PlayersController.GetByNetConnection(Msg.SenderConnection);
                                Player.Name = PlayerData.Name;
                                Player.Health = PlayerData.Health;
                                Player.Armor = PlayerData.Armor;
                                Vector3 Position = new Vector3();
                                Position.X = PlayerData.Pos_X;
                                Position.Y = PlayerData.Pos_Y;
                                Position.Z = PlayerData.Pos_Z;
                                Vector3 Velocity = new Vector3();
                                Velocity.X = PlayerData.Vel_X;
                                Velocity.Y = PlayerData.Vel_Y;
                                Velocity.Z = PlayerData.Vel_Z;
                                Quaternion Rotation = new Quaternion();
                                Rotation.X = PlayerData.Rot_X;
                                Rotation.Y = PlayerData.Rot_Y;
                                Rotation.Z = PlayerData.Rot_Z;
                                Rotation.W = PlayerData.Rot_A;
                                if (PlayerData.CurrentVehicle > 0)
                                {
                                    Vehicle Vehicle = VehiclesController.GetByID(PlayerData.CurrentVehicle);
                                    Vehicle.Position = Position;
                                    Vehicle.Velocity = Velocity;
                                    Vehicle.Rotation = Rotation;
                                    Vehicle.Heading = PlayerData.Heading;
                                    Vehicle.Speed = PlayerData.Speed;
                                    Vehicle.Driver = Player;
                                    Player.CurrentVehicle = PlayerData.CurrentVehicle;
                                    Player.VehicleSeat = PlayerData.VehicleSeat;
                                }
                                else
                                {
                                    if (Player.CurrentVehicle > 0)
                                    {
                                        Vehicle Vehicle = VehiclesController.GetByID(Player.CurrentVehicle);
                                        Vehicle.Driver = null;
                                        Player.CurrentVehicle = 0;
                                    }
                                    Player.Position = Position;
                                    Player.Velocity = Velocity;
                                    Player.Heading = PlayerData.Heading;
                                    Player.IsWalking = PlayerData.IsWalking;
                                    Player.IsRunning = PlayerData.IsRunning;
                                    Player.IsJumping = PlayerData.IsJumping;
                                    Player.IsCrouching = PlayerData.IsCrouching;
                                    Player.IsGettingIntoVehicle = PlayerData.IsGettingIntoVehicle;
                                    Player.IsGettingOutOfVehicle = PlayerData.IsGettingOutOfVehicle;
                                }

                                // Test Player

                                if (TestPlayer != null)
                                {
                                    TestPlayer.Name = "TestPlayer";
                                    TestPlayer.Health = PlayerData.Health;
                                    TestPlayer.Armor = PlayerData.Armor;
                                    Position.X = Position.X + 5;
                                    if (PlayerData.CurrentVehicle > 0)
                                    {
                                        if (PlayerData.CurrentVehicle == 1)
                                        {
                                            PlayerData.CurrentVehicle = 2;
                                        }
                                        Vehicle Vehicle = VehiclesController.GetByID(PlayerData.CurrentVehicle);
                                        Vehicle.Position = Position;
                                        Vehicle.Velocity = Velocity;
                                        Vehicle.Rotation = Rotation;
                                        Vehicle.Heading = PlayerData.Heading;
                                        Vehicle.Speed = PlayerData.Speed;
                                        Vehicle.Driver = TestPlayer;
                                        TestPlayer.CurrentVehicle = PlayerData.CurrentVehicle;
                                        TestPlayer.VehicleSeat = PlayerData.VehicleSeat;
                                    }
                                    else
                                    {
                                        if (TestPlayer.CurrentVehicle > 0)
                                        {
                                            Vehicle Vehicle = VehiclesController.GetByID(TestPlayer.CurrentVehicle);
                                            Vehicle.Driver = null;
                                            TestPlayer.CurrentVehicle = 0;
                                        }
                                        TestPlayer.Position = Position;
                                        TestPlayer.Velocity = Velocity;
                                        TestPlayer.Heading = PlayerData.Heading;
                                        TestPlayer.IsWalking = PlayerData.IsWalking;
                                        TestPlayer.IsRunning = PlayerData.IsRunning;
                                        TestPlayer.IsJumping = PlayerData.IsJumping;
                                        TestPlayer.IsCrouching = PlayerData.IsCrouching;
                                        TestPlayer.IsGettingIntoVehicle = PlayerData.IsGettingIntoVehicle;
                                        TestPlayer.IsGettingOutOfVehicle = PlayerData.IsGettingOutOfVehicle;
                                    }
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        Console.WriteLine("Unhandled message type: " + Msg.MessageType);
                        break;
                }
                NetServer.Recycle(Msg);
            }

            UpdateAllPlayers();
            UpdateAllVehicles();
        }

        public static void UpdateAllPlayers()
        {
            List<Player> Players = PlayersController.GetAll();
            foreach (var Player in Players)
            {
                NetOutgoingMessage Msg = NetServer.CreateMessage();
                Msg.Write((int)Shared.NetworkMessageType.UpdatePlayer);
                PlayerUpdateStruct PlayerData = new PlayerUpdateStruct();
                PlayerData.ID = Player.ID;
                PlayerData.Name = Player.Name;
                PlayerData.Model = Player.Model;
                PlayerData.Health = Player.Health;
                PlayerData.Armor = Player.Armor;
                PlayerData.CurrentVehicle = Player.CurrentVehicle;
                PlayerData.VehicleSeat = Player.VehicleSeat;
                PlayerData.Pos_X = Player.Position.X;
                PlayerData.Pos_Y = Player.Position.Y;
                PlayerData.Pos_Z = Player.Position.Z;
                PlayerData.Vel_X = Player.Velocity.X;
                PlayerData.Vel_Y = Player.Velocity.Y;
                PlayerData.Vel_Z = Player.Velocity.Z;
                PlayerData.Heading = Player.Heading;
                PlayerData.IsWalking = Player.IsWalking;
                PlayerData.IsRunning = Player.IsRunning;
                PlayerData.IsJumping = Player.IsJumping;
                PlayerData.IsCrouching = Player.IsCrouching;
                PlayerData.IsGettingIntoVehicle = Player.IsGettingIntoVehicle;
                PlayerData.IsGettingOutOfVehicle = Player.IsGettingOutOfVehicle;

                Msg.WriteAllFields(PlayerData);

                NetServer.SendToAll(Msg, Player.NetConnection, NetDeliveryMethod.Unreliable, 1);
            }
        }

        public static void UpdateAllVehicles()
        {
            List<Vehicle> Vehicles = VehiclesController.GetAll();
            foreach (var Vehicle in Vehicles)
            {
                NetOutgoingMessage Msg = NetServer.CreateMessage();
                VehicleUpdateStruct VehicleData = new VehicleUpdateStruct();
                VehicleData.ID = Vehicle.ID;
                VehicleData.Model = Vehicle.Model;
                VehicleData.Pos_X = Vehicle.Position.X;
                VehicleData.Pos_Y = Vehicle.Position.Y;
                VehicleData.Pos_Z = Vehicle.Position.Z;
                VehicleData.Vel_X = Vehicle.Velocity.X;
                VehicleData.Vel_Y = Vehicle.Velocity.Y;
                VehicleData.Vel_Z = Vehicle.Velocity.Z;
                VehicleData.Rot_X = Vehicle.Rotation.X;
                VehicleData.Rot_Y = Vehicle.Rotation.Y;
                VehicleData.Rot_Z = Vehicle.Rotation.Z;
                VehicleData.Rot_A = Vehicle.Rotation.W;

                VehicleData.Heading = Vehicle.Heading;
                VehicleData.Speed = Vehicle.Speed;

                Msg.Write((int)Shared.NetworkMessageType.UpdateVehicle);
                Msg.WriteAllFields(VehicleData);

                if (Vehicle.Driver == null)
                {
                    NetServer.SendToAll(Msg, NetDeliveryMethod.Unreliable);
                }
                else
                {
                    NetServer.SendToAll(Msg, Vehicle.Driver.NetConnection, NetDeliveryMethod.Unreliable, 2);
                }
            }
        }

        public static void Shutdown()
        {
            NetServer.Shutdown("Shutdown");
        }
    }
}
