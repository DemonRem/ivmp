﻿/*
 * This file is subject to the terms and conditions defined in
 * file 'LICENSE', which is part of this source code package.
 * Copyright (c) 2016 Neproify
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using Lidgren.Network;
using Shared;

namespace ivmp_client_core
{
    public class Client : Script
    {
        public Client Instance;
        public NetClient NetClient;

        public string PlayerName;
        public RemotePlayerController RemotePlayerController;
        public RemoteVehicleController RemoteVehicleController;

        public bool IsSpawned;

        public bool Initialized = false;

        public Client()
        {
            Instance = this;
            Interval = Shared.Settings.TickRate;
            KeyDown += new GTA.KeyEventHandler(OnKeyDown);
            Tick += new EventHandler(OnTick);
            PerFrameDrawing += new GraphicsEventHandler(OnFrameRender);
            PlayerName = "Player";
            NetPeerConfiguration Config = new NetPeerConfiguration("ivmp");
            Config.AutoFlushSendQueue = true;
            Config.ConnectionTimeout = 30;
            NetClient = new NetClient(Config);
            NetClient.Start();
            BindConsoleCommand("connect", ConnectCommand);
            BindConsoleCommand("disconnect", DisconnectCommand);
        }

        public void ConnectCommand(ParameterCollection Parameter)
        {
            Connect(Parameter[0], Parameter.ToInteger(1));
        }

        public void DisconnectCommand(ParameterCollection Parameter)
        {
            Disconnect();
        }

        public void Connect(string IP, int port)
        {
            if(NetClient.ConnectionStatus == NetConnectionStatus.Connected)
            {
                Disconnect();
            }
            NetOutgoingMessage Msg = NetClient.CreateMessage();
            Msg.Write(Shared.Settings.NetworkVersion);
            NetClient.Connect(IP, port, Msg);
        }

        public void Disconnect()
        {
            NetClient.Disconnect("Disconnect");
        }

        public void OnTick(object sender, EventArgs e)
        {
            if(NetClient == null)
            {
                return;
            }

            NetIncomingMessage Msg;

            while((Msg = NetClient.ReadMessage()) != null)
            {
                switch(Msg.MessageType)
                {
                    case NetIncomingMessageType.StatusChanged:
                        NetConnectionStatus status = (NetConnectionStatus)Msg.ReadByte();
                        switch(status)
                        {
                            case NetConnectionStatus.InitiatedConnect:
                                Game.Console.Print("Connecting to server.");
                                break;
                            case NetConnectionStatus.Connected:
                                Game.Console.Print("Connected to server. ID: " + NetClient.UniqueIdentifier);
                                World.CarDensity = 0;
                                World.PedDensity = 0;
                                GTA.Native.Function.Call("CLEAR_AREA", 0.0f, 0.0f, 0.0f, 4000.0f, true);
                                Game.LocalPlayer.Character.BlockGestures = true;
                                Game.LocalPlayer.Character.BlockPermanentEvents = true;
                                Game.LocalPlayer.Character.PreventRagdoll = true;
                                Game.LocalPlayer.Character.WillFlyThroughWindscreen = false;
                                Ped[] Peds = World.GetAllPeds();
                                foreach(var Ped in Peds)
                                {
                                    if(Ped != Game.LocalPlayer.Character)
                                    {
                                        Ped.Delete();
                                    }
                                }
                                Vehicle[] Vehicles = World.GetAllVehicles();
                                foreach(var Vehicle in Vehicles)
                                {
                                    Vehicle.Delete();
                                }
                                RemotePlayerController = new RemotePlayerController();
                                RemoteVehicleController = new RemoteVehicleController();
                                IsSpawned = false;
                                Game.FadeScreenOut(1);
                                Initialized = true;
                                break;
                            case NetConnectionStatus.Disconnected:
                                Game.Console.Print("Disconnected from server.");
                                break;
                        }
                        break;
                    case NetIncomingMessageType.Data:
                        int MsgType = Msg.ReadInt32();
                        switch (MsgType)
                        {
                            case (int)Shared.NetworkMessageType.PlayerConnected:
                                {
                                    break;
                                }
                            case (int)Shared.NetworkMessageType.PlayerDisconnected:
                                {
                                    long ID = Msg.ReadInt64();
                                    RemotePlayer Player = RemotePlayerController.FindByID(ID);
                                    RemotePlayerController.Remove(Player);
                                    Player.Destroy();
                                    Player = null;
                                    break;
                                }
                            case (int)Shared.NetworkMessageType.UpdatePlayer:
                                {
                                    PlayerUpdateStruct PlayerData = new PlayerUpdateStruct();
                                    PlayerData.ID = Msg.ReadInt64();
                                    PlayerData.Name = Msg.ReadString();
                                    PlayerData.Model = Msg.ReadString();
                                    PlayerData.Health = Msg.ReadInt32();
                                    PlayerData.Armor = Msg.ReadInt32();
                                    PlayerData.CurrentVehicle = Msg.ReadInt32();
                                    PlayerData.Pos_X = Msg.ReadFloat();
                                    PlayerData.Pos_Y = Msg.ReadFloat();
                                    PlayerData.Pos_Z = Msg.ReadFloat();
                                    PlayerData.Heading = Msg.ReadFloat();
                                    PlayerData.IsWalking = Msg.ReadBoolean();
                                    PlayerData.IsRunning = Msg.ReadBoolean();
                                    PlayerData.IsJumping = Msg.ReadBoolean();
                                    RemotePlayer Player = RemotePlayerController.FindByID(PlayerData.ID);
                                    Player.Name = PlayerData.Name;
                                    Player.SetHealth(PlayerData.Health);
                                    Player.SetArmor(PlayerData.Armor);
                                    if (PlayerData.CurrentVehicle > 0)
                                    {
                                        Player.CurrentVehicle = RemoteVehicleController.FindByID(PlayerData.CurrentVehicle);
                                    }
                                    else
                                    {
                                        Vector3 Position = new Vector3();
                                        Position.X = PlayerData.Pos_X;
                                        Position.Y = PlayerData.Pos_Y;
                                        Position.Z = PlayerData.Pos_Z - 1.0f;
                                        Player.SetPosition(Position);
                                        Player.SetHeading(PlayerData.Heading);
                                        Player.IsWalking = PlayerData.IsWalking;
                                        Player.IsRunning = PlayerData.IsRunning;
                                        Player.IsJumping = PlayerData.IsJumping;

                                        Player.Interpolation_Start = DateTime.Now;
                                        Player.Interpolation_End = DateTime.Now.AddMilliseconds((double)Shared.Settings.TickRate).AddMilliseconds(NetClient.ServerConnection.AverageRoundtripTime / 1000);
                                        Player.Update();
                                    }
                                    break;
                                }
                            case (int)Shared.NetworkMessageType.SpawnPlayer:
                                {
                                    Vector3 Position = new Vector3();
                                    Position.X = Msg.ReadFloat();
                                    Position.Y = Msg.ReadFloat();
                                    Position.Z = Msg.ReadFloat();
                                    float Heading = Msg.ReadFloat();
                                    IsSpawned = true;
                                    Game.LocalPlayer.Character.Position = Position;
                                    Game.LocalPlayer.Character.Heading = Heading;
                                    break;
                                }
                            case (int)Shared.NetworkMessageType.FadeScreenIn:
                                {
                                    int Duration = Msg.ReadInt32();
                                    Game.FadeScreenIn(Duration);
                                    break;
                                }
                            case (int)Shared.NetworkMessageType.FadeScreenOut:
                                {
                                    int Duration = Msg.ReadInt32();
                                    Game.FadeScreenOut(Duration);
                                    break;
                                }
                            case (int)Shared.NetworkMessageType.UpdateVehicle:
                                {
                                    VehicleUpdateStruct VehicleData = new VehicleUpdateStruct();
                                    VehicleData.ID = Msg.ReadInt32();
                                    VehicleData.Model = Msg.ReadString();
                                    VehicleData.Pos_X = Msg.ReadFloat();
                                    VehicleData.Pos_Y = Msg.ReadFloat();
                                    VehicleData.Pos_Z = Msg.ReadFloat();
                                    VehicleData.Rot_X = Msg.ReadFloat();
                                    VehicleData.Rot_Y = Msg.ReadFloat();
                                    VehicleData.Rot_Z = Msg.ReadFloat();
                                    VehicleData.Rot_A = Msg.ReadFloat();

                                    RemoteVehicle Vehicle = RemoteVehicleController.FindByID(VehicleData.ID);
                                    Vehicle.Model = VehicleData.Model;
                                    Vector3 Position = new Vector3();
                                    Position.X = VehicleData.Pos_X;
                                    Position.Y = VehicleData.Pos_Y;
                                    Position.Z = VehicleData.Pos_Z;
                                    Vehicle.SetPosition(Position);
                                    Quaternion Rotation = new Quaternion();
                                    Rotation.X = VehicleData.Rot_X;
                                    Rotation.Y = VehicleData.Rot_Y;
                                    Rotation.Z = VehicleData.Rot_Z;
                                    Rotation.W = VehicleData.Rot_A;
                                    Vehicle.SetRotation(Rotation);
                                    Vehicle.Interpolation_Start = DateTime.Now;
                                    Vehicle.Interpolation_End = DateTime.Now.AddMilliseconds((double)Shared.Settings.TickRate).AddMilliseconds(NetClient.ServerConnection.AverageRoundtripTime / 1000);
                                    break;
                                }
                            default:
                                {
                                    break;
                                }
                        }
                        break;
                    default:
                        Game.Console.Print("Unhandled message type: " + Msg.MessageType);
                        break;
                }
            }

            if (NetClient.ConnectionStatus == NetConnectionStatus.Connected)
            {
                NetOutgoingMessage OutMsg = NetClient.CreateMessage();
                PlayerUpdateStruct PlayerData = new PlayerUpdateStruct();
                Vector3 PlayerPos = Game.LocalPlayer.Character.Position;
                Vector3 PlayerRot = Game.LocalPlayer.Character.Direction;
                float PlayerHeading = Game.LocalPlayer.Character.Heading;
                PlayerData.Name = Name;
                PlayerData.Health = Game.LocalPlayer.Character.Health;
                PlayerData.Armor = Game.LocalPlayer.Character.Armor;
                if (Game.LocalPlayer.Character.isInVehicle())
                {
                    Vehicle CurrentVehicle = Game.LocalPlayer.Character.CurrentVehicle;
                    PlayerData.CurrentVehicle = RemoteVehicleController.FindByGame(CurrentVehicle).ID;
                    PlayerData.Pos_X = CurrentVehicle.Position.X;
                    PlayerData.Pos_Y = CurrentVehicle.Position.Y;
                    PlayerData.Pos_Z = CurrentVehicle.Position.Z;
                    PlayerData.Rot_X = CurrentVehicle.RotationQuaternion.X;
                    PlayerData.Rot_Y = CurrentVehicle.RotationQuaternion.Y;
                    PlayerData.Rot_Z = CurrentVehicle.RotationQuaternion.Z;
                    PlayerData.Rot_A = CurrentVehicle.RotationQuaternion.W;
                }
                else
                {
                    PlayerData.Pos_X = PlayerPos.X;
                    PlayerData.Pos_Y = PlayerPos.Y;
                    PlayerData.Pos_Z = PlayerPos.Z;
                    PlayerData.Heading = PlayerHeading;
                    PlayerData.IsWalking = Game.isGameKeyPressed(GameKey.MoveBackward) ||
                        Game.isGameKeyPressed(GameKey.MoveForward) ||
                        Game.isGameKeyPressed(GameKey.MoveLeft) ||
                        Game.isGameKeyPressed(GameKey.MoveRight);
                    PlayerData.IsRunning = Game.isGameKeyPressed(GameKey.Sprint);
                    PlayerData.IsJumping = Game.isGameKeyPressed(GameKey.Jump);
                }
                OutMsg.Write((int)NetworkMessageType.UpdatePlayer);
                OutMsg.Write(PlayerData.Name);
                OutMsg.Write(PlayerData.Health);
                OutMsg.Write(PlayerData.Armor);
                OutMsg.Write(PlayerData.CurrentVehicle);
                OutMsg.Write(PlayerData.Pos_X);
                OutMsg.Write(PlayerData.Pos_Y);
                OutMsg.Write(PlayerData.Pos_Z);
                OutMsg.Write(PlayerData.Rot_X);
                OutMsg.Write(PlayerData.Rot_Y);
                OutMsg.Write(PlayerData.Rot_Z);
                OutMsg.Write(PlayerData.Rot_A);
                OutMsg.Write(PlayerData.Heading);
                OutMsg.Write(PlayerData.IsWalking);
                OutMsg.Write(PlayerData.IsRunning);
                OutMsg.Write(PlayerData.IsJumping);
                NetClient.SendMessage(OutMsg, NetDeliveryMethod.UnreliableSequenced);
            }
        }

        public void OnKeyDown(object sender, GTA.KeyEventArgs e)
        {
            switch(e.Key)
            {
                default:
                    break;
            }
        }

        public void OnFrameRender(object sender, GraphicsEventArgs e)
        {
            if(Initialized && NetClient.ConnectionStatus == NetConnectionStatus.Connected)
            {
                List<RemotePlayer> Players = RemotePlayerController.Players;
                foreach(var Player in Players)
                {
                    Player.UpdateInterpolation();
                }

                List<RemoteVehicle> Vehicles = RemoteVehicleController.Vehicles;
                foreach(var Vehicle in Vehicles)
                {
                    bool CancelThisVehicleUpdate = false;
                    if (Game.LocalPlayer.Character.isInVehicle())
                    {
                        if (Game.LocalPlayer.Character.CurrentVehicle == Vehicle.Vehicle)
                        {
                            CancelThisVehicleUpdate = true;
                        }
                    }
                    if (CancelThisVehicleUpdate == false)
                    {
                        Vehicle.UpdateInterpolation();
                    }
                }
            }
        }
    }
}
