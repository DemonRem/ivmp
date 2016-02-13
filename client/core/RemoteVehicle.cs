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
using GTA;

namespace ivmp_client_core
{
    public class RemoteVehicle
    {
        public Vehicle vehicle;

        public int ID;
        public string Model;

        public float Pos_X;
        public float Pos_Y;
        public float Pos_Z;

        public float Rot_X;
        public float Rot_Y;
        public float Rot_Z;

        public RemoteVehicle(string Model)
        {
            this.Model = Model;
            vehicle = World.CreateVehicle(Model, Vector3.Zero);
        }
    }
}
