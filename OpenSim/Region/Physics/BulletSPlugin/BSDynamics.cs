/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 * The quotations from http://wiki.secondlife.com/wiki/Linden_Vehicle_Tutorial
 * are Copyright (c) 2009 Linden Research, Inc and are used under their license
 * of Creative Commons Attribution-Share Alike 3.0
 * (http://creativecommons.org/licenses/by-sa/3.0/).
 */

using OpenMetaverse;
using OpenSim.Region.Physics.Manager;
using System;

namespace OpenSim.Region.Physics.BulletSPlugin
{
    public sealed class BSDynamics : BSActor
    {
#pragma warning disable 414
        private static string LogHeader = "[BULLETSIM VEHICLE]";
#pragma warning restore 414

        // the prim this dynamic controller belongs to
        private BSPrimLinkable ControllingPrim { get; set; }

        private sealed class ReferenceBoxed<T> where T : struct
        {
            private T Value;

            public ReferenceBoxed()
            {
            }

            public ReferenceBoxed(T value)
            {
                Value = value;
            }

            public static implicit operator T(ReferenceBoxed<T> reference) => reference?.Value ?? default(T);

            public static implicit operator ReferenceBoxed<T>(T value) => new ReferenceBoxed<T>(value);
        }

        private bool m_haveRegisteredForSceneEvents;

        // mass of the vehicle fetched each time we're calles
        private float m_vehicleMass;

        // Vehicle properties
        public Vehicle Type { get; set; }

        // private Quaternion m_referenceFrame = Quaternion.Identity;   // Axis modifier
        private VehicleFlag m_flags = (VehicleFlag) 0;                  // Boolean settings:
                                                                        // HOVER_TERRAIN_ONLY
                                                                        // HOVER_GLOBAL_HEIGHT
                                                                        // NO_DEFLECTION_UP
                                                                        // HOVER_WATER_ONLY
                                                                        // HOVER_UP_ONLY
                                                                        // LIMIT_MOTOR_UP
                                                                        // LIMIT_ROLL_ONLY
        private Vector3 m_BlockingEndPoint = Vector3.Zero;
        private ReferenceBoxed<Quaternion> m_RollreferenceFrame = Quaternion.Identity;
        private ReferenceBoxed<Quaternion> m_referenceFrame = Quaternion.Identity;

        // Linear properties
        private ReferenceBoxed<Vector3> m_linearMotorNewDirection = Vector3.Zero;          // velocity requested by LSL, decayed by time
        private bool m_linearMotorNewDirectionApply = false;
        private ReferenceBoxed<Vector3> m_linearMotorDecayingDirection = Vector3.Zero;
        private ReferenceBoxed<Vector3> m_linearMotorOffset = Vector3.Zero;             // the point of force can be offset from the center
        private ReferenceBoxed<Vector3> m_linearFrictionTimescale = Vector3.Zero;
        private float m_linearMotorDecayTimescale = 1;
        private float m_linearMotorTimescale = 1;
        private Vector3 m_lastPositionVector = Vector3.Zero;

        //Angular properties
        // private int m_angularMotorApply = 0;                            // application frame counter
        //Angular properties
        private ReferenceBoxed<Vector3> m_angularMotorNewDirection = Vector3.Zero;         // angular velocity requested by LSL motor
        private bool m_angularMotorNewDirectionApply = false;
        private ReferenceBoxed<Vector3> m_angularMotorDecayingDirection = Vector3.Zero;         // angular velocity requested by LSL motor
        // private int m_angularMotorApply = 0;                            // application frame counter
        private float m_angularMotorTimescale = 1;                      // motor angular velocity ramp up rate
        private float m_angularMotorDecayTimescale = 1;                 // motor angular velocity decay rate
        private ReferenceBoxed<Vector3> m_angularFrictionTimescale = Vector3.Zero;      // body angular velocity  decay rate
        private ReferenceBoxed<Vector3> m_lastAngularVelocity = Vector3.Zero;

        //Deflection properties
        private float m_angularDeflectionEfficiency = 0;
        private float m_angularDeflectionTimescale = 1;
        private float m_linearDeflectionEfficiency = 0;
        private float m_linearDeflectionTimescale = 1;

        //Banking properties
        private float m_bankingEfficiency = 0;
        private float m_bankingMix = 0;
        private float m_bankingTimescale = 1;

        //Hover and Buoyancy properties
        private float m_VhoverHeight = 0f;
        private float m_VhoverEfficiency = 0f;
        private float m_VhoverTimescale = 310f;
        private float m_VhoverTargetHeight = 0.0f;     // if <0 then no hover, else its the current target height
        // Modifies gravity. Slider between -1 (double-gravity) and 1 (full anti-gravity)
        private float m_VehicleBuoyancy = 0f;
        private Vector3 m_VehicleGravity = Vector3.Zero;    // Gravity computed when buoyancy set

        //Attractor properties
        private float m_verticalAttractionEfficiency = 1.0f; // damped
        private float m_verticalAttractionCutoff = 500f;     // per the documentation
        // Timescale > cutoff  means no vert attractor.
        private float m_verticalAttractionTimescale = 510f;

        // Just some recomputed constants:
        static readonly float PIOverFour = ((float)Math.PI) / 4f;
#pragma warning disable 414
        static readonly float PIOverTwo = ((float)Math.PI) / 2f;
#pragma warning restore 414

        public BSDynamics(BSScene myScene, BSPrim myPrim, string actorName)
            : base(myScene, myPrim, actorName)
        {
            Type = Vehicle.TYPE_NONE;
            m_haveRegisteredForSceneEvents = false;

            ControllingPrim = (BSPrimLinkable)myPrim;
        }

        // Return 'true' if this vehicle is doing vehicle things
        public bool IsActive
        {
            get { return (Type != Vehicle.TYPE_NONE && ControllingPrim.IsPhysicallyActive); }
        }

        // Return 'true' if this a vehicle that should be sitting on the ground
        public bool IsGroundVehicle
        {
            get { return (Type == Vehicle.TYPE_CAR || Type == Vehicle.TYPE_SLED); }
        }

        #region Vehicle parameter setting
        public void ProcessFloatVehicleParam(Vehicle pParam, float pValue)
        {
            float clampTemp;
            switch (pParam)
            {
                case Vehicle.ANGULAR_DEFLECTION_EFFICIENCY:
                    m_angularDeflectionEfficiency = ClampInRange(0f, pValue, 1f);
                    break;
                case Vehicle.ANGULAR_DEFLECTION_TIMESCALE:
                    m_angularDeflectionTimescale = Math.Max(pValue, 0.01f);
                    break;
                case Vehicle.ANGULAR_MOTOR_DECAY_TIMESCALE:
                    m_angularMotorDecayTimescale = ClampInRange(0.01f, pValue, 120);
                    break;
                case Vehicle.ANGULAR_MOTOR_TIMESCALE:
                    m_angularMotorTimescale = Math.Max(pValue, 0.01f);
                    break;
                case Vehicle.BANKING_EFFICIENCY:
                    m_bankingEfficiency = ClampInRange(-1f, pValue, 1f);
                    break;
                case Vehicle.BANKING_MIX:
                    m_bankingMix = Math.Max(pValue, 0.01f);
                    break;
                case Vehicle.BANKING_TIMESCALE:
                    m_bankingTimescale = Math.Max(pValue, 0.01f);
                    break;
                case Vehicle.BUOYANCY:
                    m_VehicleBuoyancy = ClampInRange(-1f, pValue, 1f);
                    m_VehicleGravity = ControllingPrim.ComputeGravity(m_VehicleBuoyancy);
                    break;
                case Vehicle.HOVER_EFFICIENCY:
                    m_VhoverEfficiency = ClampInRange(0f, pValue, 1f);
                    break;
                case Vehicle.HOVER_HEIGHT:
                    m_VhoverHeight = pValue;
                    break;
                case Vehicle.HOVER_TIMESCALE:
                    m_VhoverTimescale = Math.Max(pValue, 0.01f);
                    break;
                case Vehicle.LINEAR_DEFLECTION_EFFICIENCY:
                    m_linearDeflectionEfficiency = ClampInRange(0f, pValue, 1f);
                    break;
                case Vehicle.LINEAR_DEFLECTION_TIMESCALE:
                    m_linearDeflectionTimescale = Math.Max(pValue, 0.01f);
                    break;
                case Vehicle.LINEAR_MOTOR_DECAY_TIMESCALE:
                    m_linearMotorDecayTimescale = ClampInRange(0.01f, pValue, 120);
                    break;
                case Vehicle.LINEAR_MOTOR_TIMESCALE:
                    m_linearMotorTimescale = Math.Max(pValue, 0.01f);
                    break;
                case Vehicle.VERTICAL_ATTRACTION_EFFICIENCY:
                    m_verticalAttractionEfficiency = ClampInRange(0.1f, pValue, 1f);
                    break;
                case Vehicle.VERTICAL_ATTRACTION_TIMESCALE:
                    m_verticalAttractionTimescale = Math.Max(pValue, 0.01f);
                    break;

                // These are vector properties but the engine lets you use a single float value to
                // set all of the components to the same value
                case Vehicle.ANGULAR_FRICTION_TIMESCALE:
                    m_angularFrictionTimescale = new Vector3(pValue, pValue, pValue);
                    break;
                case Vehicle.ANGULAR_MOTOR_DIRECTION:
                    m_angularMotorNewDirection = new Vector3(pValue, pValue, pValue);
                    m_angularMotorNewDirectionApply = true;
                    break;
                case Vehicle.LINEAR_FRICTION_TIMESCALE:
                    clampTemp = Math.Max(pValue, 0.01f);
                    m_linearFrictionTimescale = new Vector3(clampTemp, clampTemp, clampTemp);
                    break;
                case Vehicle.LINEAR_MOTOR_DIRECTION:
                    m_linearMotorNewDirection = new Vector3(pValue, pValue, pValue);
                    m_linearMotorNewDirectionApply = true;
                    break;
                case Vehicle.LINEAR_MOTOR_OFFSET:
                    m_linearMotorOffset = new Vector3(pValue, pValue, pValue);
                    break;

            }
        }//end ProcessFloatVehicleParam

        internal void ProcessVectorVehicleParam(Vehicle pParam, Vector3 pValue)
        {
            switch (pParam)
            {
                case Vehicle.ANGULAR_FRICTION_TIMESCALE:
                    pValue.X = Math.Max(pValue.X, 0.01f);
                    pValue.Y = Math.Max(pValue.Y, 0.01f);
                    pValue.Z = Math.Max(pValue.Z, 0.01f);
                    m_angularFrictionTimescale = new Vector3(pValue.X, pValue.Y, pValue.Z);
                    break;
                case Vehicle.ANGULAR_MOTOR_DIRECTION:
                    // Limit requested angular speed to 2 rps= 4 pi rads/sec
                    pValue.X = ClampInRange(-12.56f, pValue.X, 12.56f);
                    pValue.Y = ClampInRange(-12.56f, pValue.Y, 12.56f);
                    pValue.Z = ClampInRange(-12.56f, pValue.Z, 12.56f);
                    m_angularMotorNewDirection = new Vector3(pValue.X, pValue.Y, pValue.Z);
                    m_angularMotorNewDirectionApply = true;
                    break;
                case Vehicle.LINEAR_FRICTION_TIMESCALE:
                    pValue.X = Math.Max(pValue.X, 0.01f);
                    pValue.Y = Math.Max(pValue.Y, 0.01f);
                    pValue.Z = Math.Max(pValue.Z, 0.01f);
                    m_linearFrictionTimescale = new Vector3(pValue.X, pValue.Y, pValue.Z);
                    break;
                case Vehicle.LINEAR_MOTOR_DIRECTION:
                    m_linearMotorNewDirection = new Vector3(pValue.X, pValue.Y, pValue.Z);
                    m_linearMotorNewDirectionApply = true;
                    break;
                case Vehicle.LINEAR_MOTOR_OFFSET:
                    m_linearMotorOffset = new Vector3(pValue.X, pValue.Y, pValue.Z);
                    break;
                case Vehicle.BLOCK_EXIT:
                    m_BlockingEndPoint = new Vector3(pValue.X, pValue.Y, pValue.Z);
                    break;
            }
        }//end ProcessVectorVehicleParam

        internal void ProcessRotationVehicleParam(Vehicle pParam, Quaternion pValue)
        {
            switch (pParam)
            {
                case Vehicle.REFERENCE_FRAME:
                    m_referenceFrame = pValue;
                    break;
                case Vehicle.ROLL_FRAME:
                    m_RollreferenceFrame = pValue;
                    break;
            }
        }//end ProcessRotationVehicleParam

        internal void ProcessVehicleFlags(int pParam, bool remove)
        {
            VehicleFlag parm = (VehicleFlag)pParam;
            if (pParam == -1)
                m_flags = (VehicleFlag)0;
            else
            {
                if (remove)
                    m_flags &= ~parm;
                else
                    m_flags |= parm;
            }
        }

        public void ProcessTypeChange(Vehicle pType)
        {
            // Set Defaults For Type
            Type = pType;
            m_linearMotorNewDirection = Vector3.Zero;
            m_linearMotorNewDirectionApply = true;
            m_angularMotorNewDirection = Vector3.Zero;
            m_angularMotorNewDirectionApply = true;
            m_linearMotorDecayingDirection = Vector3.Zero;
            m_angularMotorDecayingDirection = Vector3.Zero;

            switch (pType)
            {
                case Vehicle.TYPE_NONE:
                    m_linearMotorTimescale = 1000;
                    m_linearMotorDecayTimescale = 120;
                    m_linearFrictionTimescale = new Vector3(1000, 1000, 1000);

                    m_angularMotorDecayTimescale = 120;
                    m_angularMotorTimescale = 1000;
                    m_angularFrictionTimescale = new Vector3(1000, 1000, 1000);

                    m_VhoverHeight = 0;
                    m_VhoverEfficiency = 0;
                    m_VhoverTimescale = 310;
                    m_VehicleBuoyancy = 0;

                    m_linearDeflectionEfficiency = 1;
                    m_linearDeflectionTimescale = 1;

                    m_angularDeflectionEfficiency = 0;
                    m_angularDeflectionTimescale = 1000;

                    m_verticalAttractionEfficiency = 0;
                    m_verticalAttractionTimescale = 1000;

                    m_bankingEfficiency = 0;
                    m_bankingTimescale = 1000;
                    m_bankingMix = 1;

                    m_referenceFrame = Quaternion.Identity;
                    m_flags = (VehicleFlag)0;

                    break;

                case Vehicle.TYPE_SLED:
                    m_linearMotorTimescale = 1000;
                    m_linearMotorDecayTimescale = 120;
                    m_linearFrictionTimescale = new Vector3(30, 1, 1000);

                    m_angularMotorTimescale = 1000;
                    m_angularMotorDecayTimescale = 120;
                    m_angularFrictionTimescale = new Vector3(1000, 1000, 1000);

                    m_VhoverHeight = 0;
                    m_VhoverEfficiency = 10;    // TODO: this looks wrong!!
                    m_VhoverTimescale = 10;
                    m_VehicleBuoyancy = 0;

                    m_linearDeflectionEfficiency = 1;
                    m_linearDeflectionTimescale = 1;

                    m_angularDeflectionEfficiency = 0;
                    m_angularDeflectionTimescale = 10;

                    m_verticalAttractionEfficiency = 1;
                    m_verticalAttractionTimescale = 1000;

                    m_bankingEfficiency = 0;
                    m_bankingTimescale = 10;
                    m_bankingMix = 1;

                    m_referenceFrame = Quaternion.Identity;
                    m_flags &= ~(VehicleFlag.HOVER_WATER_ONLY
                                | VehicleFlag.HOVER_TERRAIN_ONLY
                                | VehicleFlag.HOVER_GLOBAL_HEIGHT
                                | VehicleFlag.HOVER_UP_ONLY);
                    m_flags |= (VehicleFlag.NO_DEFLECTION_UP
                            | VehicleFlag.LIMIT_ROLL_ONLY
                            | VehicleFlag.LIMIT_MOTOR_UP);

                    break;
                case Vehicle.TYPE_CAR:
                    m_linearMotorTimescale = 1;
                    m_linearMotorDecayTimescale = 60;
                    m_linearFrictionTimescale = new Vector3(100, 2, 1000);

                    m_angularMotorTimescale = 1;
                    m_angularMotorDecayTimescale = 0.8f;
                    m_angularFrictionTimescale = new Vector3(1000, 1000, 1000);

                    m_VhoverHeight = 0;
                    m_VhoverEfficiency = 0;
                    m_VhoverTimescale = 1000;
                    m_VehicleBuoyancy = 0;

                    m_linearDeflectionEfficiency = 1;
                    m_linearDeflectionTimescale = 2;

                    m_angularDeflectionEfficiency = 0;
                    m_angularDeflectionTimescale = 10;

                    m_verticalAttractionEfficiency = 1f;
                    m_verticalAttractionTimescale = 10f;

                    m_bankingEfficiency = -0.2f;
                    m_bankingMix = 1;
                    m_bankingTimescale = 1;

                    m_referenceFrame = Quaternion.Identity;
                    m_flags &= ~(VehicleFlag.HOVER_WATER_ONLY
                                | VehicleFlag.HOVER_TERRAIN_ONLY
                                | VehicleFlag.HOVER_GLOBAL_HEIGHT);
                    m_flags |= (VehicleFlag.NO_DEFLECTION_UP
                                | VehicleFlag.LIMIT_ROLL_ONLY
                                | VehicleFlag.LIMIT_MOTOR_UP
                                | VehicleFlag.HOVER_UP_ONLY);
                    break;
                case Vehicle.TYPE_BOAT:
                    m_linearMotorTimescale = 5;
                    m_linearMotorDecayTimescale = 60;
                    m_linearFrictionTimescale = new Vector3(10, 3, 2);

                    m_angularMotorTimescale = 4;
                    m_angularMotorDecayTimescale = 4;
                    m_angularFrictionTimescale = new Vector3(10,10,10);

                    m_VhoverHeight = 0;
                    m_VhoverEfficiency = 0.5f;
                    m_VhoverTimescale = 2;
                    m_VehicleBuoyancy = 1;

                    m_linearDeflectionEfficiency = 0.5f;
                    m_linearDeflectionTimescale = 3;

                    m_angularDeflectionEfficiency = 0.5f;
                    m_angularDeflectionTimescale = 5;

                    m_verticalAttractionEfficiency = 0.5f;
                    m_verticalAttractionTimescale = 5f;

                    m_bankingEfficiency = -0.3f;
                    m_bankingMix = 0.8f;
                    m_bankingTimescale = 1;

                    m_referenceFrame = Quaternion.Identity;
                    m_flags &= ~(VehicleFlag.HOVER_TERRAIN_ONLY
                                    | VehicleFlag.HOVER_GLOBAL_HEIGHT
                                    | VehicleFlag.LIMIT_ROLL_ONLY);
                    m_flags |= (VehicleFlag.NO_DEFLECTION_UP
                                    | VehicleFlag.LIMIT_MOTOR_UP
                                    | VehicleFlag.HOVER_WATER_ONLY
                                    | VehicleFlag.HOVER_UP_ONLY);
                    break;
                case Vehicle.TYPE_AIRPLANE:
                    m_linearMotorTimescale = 2;
                    m_linearMotorDecayTimescale = 60;
                    m_linearFrictionTimescale = new Vector3(200, 10, 5);

                    m_angularMotorTimescale = 4;
                    m_angularMotorDecayTimescale = 8;
                    m_angularFrictionTimescale = new Vector3(20, 20, 20);

                    m_VhoverHeight = 0;
                    m_VhoverEfficiency = 0.5f;
                    m_VhoverTimescale = 1000;
                    m_VehicleBuoyancy = 0;

                    m_linearDeflectionEfficiency = 0.5f;
                    m_linearDeflectionTimescale = 0.5f;

                    m_angularDeflectionEfficiency = 1;
                    m_angularDeflectionTimescale = 2;

                    m_verticalAttractionEfficiency = 0.9f;
                    m_verticalAttractionTimescale = 2f;

                    m_bankingEfficiency = 1;
                    m_bankingMix = 0.7f;
                    m_bankingTimescale = 2;

                    m_referenceFrame = Quaternion.Identity;
                    m_flags &= ~(VehicleFlag.HOVER_WATER_ONLY
                                    | VehicleFlag.HOVER_TERRAIN_ONLY
                                    | VehicleFlag.HOVER_GLOBAL_HEIGHT
                                    | VehicleFlag.HOVER_UP_ONLY
                                    | VehicleFlag.NO_DEFLECTION_UP
                                    | VehicleFlag.LIMIT_MOTOR_UP);
                    m_flags |= (VehicleFlag.LIMIT_ROLL_ONLY);
                    break;
                case Vehicle.TYPE_BALLOON:
                    m_linearMotorTimescale = 5;
                    m_linearFrictionTimescale = new Vector3(5, 5, 5);
                    m_linearMotorDecayTimescale = 60;

                    m_angularMotorTimescale = 6;
                    m_angularFrictionTimescale = new Vector3(10, 10, 10);
                    m_angularMotorDecayTimescale = 10;

                    m_VhoverHeight = 5;
                    m_VhoverEfficiency = 0.8f;
                    m_VhoverTimescale = 10;
                    m_VehicleBuoyancy = 1;

                    m_linearDeflectionEfficiency = 0;
                    m_linearDeflectionTimescale = 5;

                    m_angularDeflectionEfficiency = 0;
                    m_angularDeflectionTimescale = 5;

                    m_verticalAttractionEfficiency = 1f;
                    m_verticalAttractionTimescale = 1000f;

                    m_bankingEfficiency = 0;
                    m_bankingMix = 0.7f;
                    m_bankingTimescale = 5;

                    m_referenceFrame = Quaternion.Identity;

                    m_referenceFrame = Quaternion.Identity;
                    m_flags &= ~(VehicleFlag.HOVER_WATER_ONLY
                                    | VehicleFlag.HOVER_TERRAIN_ONLY
                                    | VehicleFlag.HOVER_UP_ONLY
                                    | VehicleFlag.LIMIT_ROLL_ONLY
                                    | VehicleFlag.NO_DEFLECTION_UP
                                    | VehicleFlag.HOVER_GLOBAL_HEIGHT
                                    | VehicleFlag.LIMIT_MOTOR_UP);
                    break;
            }

            if (this.Type == Vehicle.TYPE_NONE)
            {
                UnregisterForSceneEvents();
            }
            else
            {
                RegisterForSceneEvents();
            }

            // Update any physical parameters based on this type.
            Refresh();
        }
        #endregion // Vehicle parameter setting

        // BSActor.Refresh()
        public override void Refresh()
        {
            // If asking for a refresh, reset the physical parameters before the next simulation step.
            // Called whether active or not since the active state may be updated before the next step.
            m_physicsScene.PostTaintObject("BSDynamics.Refresh", ControllingPrim.LocalID, delegate()
            {
                SetPhysicalParameters();
            });
        }

        // Some of the properties of this prim may have changed.
        // Do any updating needed for a vehicle
        private void SetPhysicalParameters()
        {
            if (IsActive)
            {
                // Remember the mass so we don't have to fetch it every step
                m_vehicleMass = ControllingPrim.TotalMass;

                // Friction affects are handled by this vehicle code
                // m_physicsScene.PE.SetFriction(ControllingPrim.PhysBody, BSParam.VehicleFriction);
                // m_physicsScene.PE.SetRestitution(ControllingPrim.PhysBody, BSParam.VehicleRestitution);
                ControllingPrim.Linkset.SetPhysicalFriction(BSParam.VehicleFriction);
                ControllingPrim.Linkset.SetPhysicalRestitution(BSParam.VehicleRestitution);

                // Moderate angular movement introduced by Bullet.
                // TODO: possibly set AngularFactor and LinearFactor for the type of vehicle.
                //     Maybe compute linear and angular factor and damping from params.
                m_physicsScene.PE.SetAngularDamping(ControllingPrim.PhysBody, BSParam.VehicleAngularDamping);
                m_physicsScene.PE.SetLinearFactor(ControllingPrim.PhysBody, BSParam.VehicleLinearFactor);
                m_physicsScene.PE.SetAngularFactorV(ControllingPrim.PhysBody, BSParam.VehicleAngularFactor);

                // Vehicles report collision events so we know when it's on the ground
                // m_physicsScene.PE.AddToCollisionFlags(ControllingPrim.PhysBody, CollisionFlags.BS_VEHICLE_COLLISIONS);
                ControllingPrim.Linkset.AddToPhysicalCollisionFlags(CollisionFlags.BS_VEHICLE_COLLISIONS);

                // Vector3 inertia = m_physicsScene.PE.CalculateLocalInertia(ControllingPrim.PhysShape.physShapeInfo, m_vehicleMass);
                // ControllingPrim.Inertia = inertia * BSParam.VehicleInertiaFactor;
                // m_physicsScene.PE.SetMassProps(ControllingPrim.PhysBody, m_vehicleMass, ControllingPrim.Inertia);
                // m_physicsScene.PE.UpdateInertiaTensor(ControllingPrim.PhysBody);
                ControllingPrim.Linkset.ComputeAndSetLocalInertia(BSParam.VehicleInertiaFactor, m_vehicleMass);

                // Set the gravity for the vehicle depending on the buoyancy
                // TODO: what should be done if prim and vehicle buoyancy differ?
                m_VehicleGravity = ControllingPrim.ComputeGravity(m_VehicleBuoyancy);
                // The actual vehicle gravity is set to zero in Bullet so we can do all the application of same.
                // m_physicsScene.PE.SetGravity(ControllingPrim.PhysBody, Vector3.Zero);
                ControllingPrim.Linkset.SetPhysicalGravity(Vector3.Zero);
            }
            else
            {
                if (ControllingPrim.PhysBody.HasPhysicalBody)
                    m_physicsScene.PE.RemoveFromCollisionFlags(ControllingPrim.PhysBody, CollisionFlags.BS_VEHICLE_COLLISIONS);
                // ControllingPrim.Linkset.RemoveFromPhysicalCollisionFlags(CollisionFlags.BS_VEHICLE_COLLISIONS);
            }
        }

        // BSActor.RemoveBodyDependencies
        public override void RemoveDependencies()
        {
            Refresh();
        }

        // BSActor.Release()
        public override void Dispose()
        {
            UnregisterForSceneEvents();
            Type = Vehicle.TYPE_NONE;
            Enabled = false;
            return;
        }

        private void RegisterForSceneEvents()
        {
            if (!m_haveRegisteredForSceneEvents)
            {
                m_physicsScene.BeforeStep += this.Step;
                m_physicsScene.AfterStep += this.PostStep;
                ControllingPrim.OnPreUpdateProperty += this.PreUpdateProperty;
                m_haveRegisteredForSceneEvents = true;
            }
        }

        private void UnregisterForSceneEvents()
        {
            if (m_haveRegisteredForSceneEvents)
            {
                m_physicsScene.BeforeStep -= this.Step;
                m_physicsScene.AfterStep -= this.PostStep;
                ControllingPrim.OnPreUpdateProperty -= this.PreUpdateProperty;
                m_haveRegisteredForSceneEvents = false;
            }
        }

        private void PreUpdateProperty(ref EntityProperties entprop)
        {
            // A temporary kludge to suppress the rotational effects introduced on vehicles by Bullet
            // TODO: handle physics introduced by Bullet with computed vehicle physics.
            if (IsActive)
            {
                entprop.RotationalVelocity = Vector3.Zero;
            }
        }

        #region Known vehicle value functions
        // Vehicle physical parameters that we buffer from constant getting and setting.
        // The "m_known*" values are unknown until they are fetched and the m_knownHas flag is set.
        //      Changing is remembered and the parameter is stored back into the physics engine only if updated.
        //      This does two things: 1) saves continuious calls into unmanaged code, and
        //      2) signals when a physics property update must happen back to the simulator
        //      to update values modified for the vehicle.
        private int m_knownChanged;
        private int m_knownHas;
        private float m_knownTerrainHeight;
        private float m_knownWaterLevel;
        private Vector3 m_knownPosition;
        private Vector3 m_knownVelocity;
        private Vector3 m_knownForce;
        private Vector3 m_knownForceImpulse;
        private Quaternion m_knownOrientation;
        private Vector3 m_knownRotationalVelocity;
        private Vector3 m_knownRotationalForce;
        private Vector3 m_knownRotationalImpulse;

        private const int m_knownChangedPosition           = 1 << 0;
        private const int m_knownChangedVelocity           = 1 << 1;
        private const int m_knownChangedForce              = 1 << 2;
        private const int m_knownChangedForceImpulse       = 1 << 3;
        private const int m_knownChangedOrientation        = 1 << 4;
        private const int m_knownChangedRotationalVelocity = 1 << 5;
        private const int m_knownChangedRotationalForce    = 1 << 6;
        private const int m_knownChangedRotationalImpulse  = 1 << 7;
        private const int m_knownChangedTerrainHeight      = 1 << 8;
        private const int m_knownChangedWaterLevel         = 1 << 9;

        public void ForgetKnownVehicleProperties()
        {
            m_knownHas = 0;
            m_knownChanged = 0;
        }
        // Push all the changed values back into the physics engine
        public void PushKnownChanged()
        {
            if (m_knownChanged != 0)
            {
                if ((m_knownChanged & m_knownChangedPosition) != 0)
                    ControllingPrim.ForcePosition = m_knownPosition;

                if ((m_knownChanged & m_knownChangedOrientation) != 0)
                    ControllingPrim.ForceOrientation = m_knownOrientation;

                if ((m_knownChanged & m_knownChangedVelocity) != 0)
                {
                    ControllingPrim.ForceVelocity = m_knownVelocity;
                    // Fake out Bullet by making it think the velocity is the same as last time.
                    // Bullet does a bunch of smoothing for changing parameters.
                    //    Since the vehicle is demanding this setting, we override Bullet's smoothing
                    //    by telling Bullet the value was the same last time.
                    // PhysicsScene.PE.SetInterpolationLinearVelocity(Prim.PhysBody, m_knownVelocity);
                }

                if ((m_knownChanged & m_knownChangedForce) != 0)
                    ControllingPrim.AddForce((Vector3)m_knownForce, false /*pushForce*/, true /*inTaintTime*/);

                if ((m_knownChanged & m_knownChangedForceImpulse) != 0)
                    ControllingPrim.AddForceImpulse((Vector3)m_knownForceImpulse, false /*pushforce*/, true /*inTaintTime*/);

                if ((m_knownChanged & m_knownChangedRotationalVelocity) != 0)
                {
                    ControllingPrim.ForceRotationalVelocity = m_knownRotationalVelocity;
                    // PhysicsScene.PE.SetInterpolationAngularVelocity(Prim.PhysBody, m_knownRotationalVelocity);
                }

                if ((m_knownChanged & m_knownChangedRotationalImpulse) != 0)
                    ControllingPrim.ApplyTorqueImpulse((Vector3)m_knownRotationalImpulse, true /*inTaintTime*/);

                if ((m_knownChanged & m_knownChangedRotationalForce) != 0)
                {
                    ControllingPrim.AddAngularForce((Vector3)m_knownRotationalForce, false /*pushForce*/, true /*inTaintTime*/);
                }

                // If we set one of the values (ie, the physics engine didn't do it) we must force
                //      an UpdateProperties event to send the changes up to the simulator.
                m_physicsScene.PE.PushUpdate(ControllingPrim.PhysBody);
            }
            m_knownChanged = 0;
        }

        // Since the computation of terrain height can be a little involved, this routine
        //    is used to fetch the height only once for each vehicle simulation step.
        Vector3 lastRememberedHeightPos = new Vector3(-1, -1, -1);
        private float GetTerrainHeight(Vector3 pos)
        {
            if ((m_knownHas & m_knownChangedTerrainHeight) == 0 || pos != lastRememberedHeightPos)
            {
                lastRememberedHeightPos = pos;
                m_knownTerrainHeight = ControllingPrim.PhysScene.TerrainManager.GetTerrainHeightAtXYZ(pos);
                m_knownHas |= m_knownChangedTerrainHeight;
            }
            return m_knownTerrainHeight;
        }

        // Since the computation of water level can be a little involved, this routine
        //    is used ot fetch the level only once for each vehicle simulation step.
        Vector3 lastRememberedWaterHeightPos = new Vector3(-1, -1, -1);
        private float GetWaterLevel(Vector3 pos)
        {
            if ((m_knownHas & m_knownChangedWaterLevel) == 0 || pos != lastRememberedWaterHeightPos)
            {
                lastRememberedWaterHeightPos = pos;
                m_knownWaterLevel = ControllingPrim.PhysScene.TerrainManager.GetWaterLevelAtXYZ(pos);
                m_knownHas |= m_knownChangedWaterLevel;
            }
            return m_knownWaterLevel;
        }

        private Vector3 VehiclePosition
        {
            get
            {
                if ((m_knownHas & m_knownChangedPosition) == 0)
                {
                    m_knownPosition = ControllingPrim.ForcePosition;
                    m_knownHas |= m_knownChangedPosition;
                }
                return m_knownPosition;
            }
            set
            {
                m_knownPosition = value;
                m_knownChanged |= m_knownChangedPosition;
                m_knownHas |= m_knownChangedPosition;
            }
        }

        private Quaternion VehicleOrientation
        {
            get
            {
                if ((m_knownHas & m_knownChangedOrientation) == 0)
                {
                    m_knownOrientation = ControllingPrim.ForceOrientation;
                    m_knownHas |= m_knownChangedOrientation;
                }
                return m_knownOrientation;
            }
            set
            {
                m_knownOrientation = value;
                m_knownChanged |= m_knownChangedOrientation;
                m_knownHas |= m_knownChangedOrientation;
            }
        }

        private Vector3 VehicleVelocity
        {
            get
            {
                if ((m_knownHas & m_knownChangedVelocity) == 0)
                {
                    m_knownVelocity = ControllingPrim.ForceVelocity;
                    m_knownHas |= m_knownChangedVelocity;
                }
                return m_knownVelocity;
            }
            set
            {
                m_knownVelocity = value;
                m_knownChanged |= m_knownChangedVelocity;
                m_knownHas |= m_knownChangedVelocity;
            }
        }

        private void VehicleAddForce(Vector3 pForce)
        {
            if ((m_knownHas & m_knownChangedForce) == 0)
            {
                m_knownForce = Vector3.Zero;
                m_knownHas |= m_knownChangedForce;
            }
            m_knownForce += pForce;
            m_knownChanged |= m_knownChangedForce;
        }

        private void VehicleAddForceImpulse(Vector3 pImpulse)
        {
            if ((m_knownHas & m_knownChangedForceImpulse) == 0)
            {
                m_knownForceImpulse = Vector3.Zero;
                m_knownHas |= m_knownChangedForceImpulse;
            }
            m_knownForceImpulse += pImpulse;
            m_knownChanged |= m_knownChangedForceImpulse;
        }

        private Vector3 VehicleRotationalVelocity
        {
            get
            {
                if ((m_knownHas & m_knownChangedRotationalVelocity) == 0)
                {
                    m_knownRotationalVelocity = ControllingPrim.ForceRotationalVelocity;
                    m_knownHas |= m_knownChangedRotationalVelocity;
                }
                return (Vector3)m_knownRotationalVelocity;
            }
            set
            {
                m_knownRotationalVelocity = value;
                m_knownChanged |= m_knownChangedRotationalVelocity;
                m_knownHas |= m_knownChangedRotationalVelocity;
            }
        }
        private void VehicleAddAngularForce(Vector3 aForce)
        {
            if ((m_knownHas & m_knownChangedRotationalForce) == 0)
            {
                m_knownRotationalForce = Vector3.Zero;
            }
            m_knownRotationalForce += aForce;
            m_knownChanged |= m_knownChangedRotationalForce;
            m_knownHas |= m_knownChangedRotationalForce;
        }
        private void VehicleAddRotationalImpulse(Vector3 pImpulse)
        {
            if ((m_knownHas & m_knownChangedRotationalImpulse) == 0)
            {
                m_knownRotationalImpulse = Vector3.Zero;
                m_knownHas |= m_knownChangedRotationalImpulse;
            }
            m_knownRotationalImpulse += pImpulse;
            m_knownChanged |= m_knownChangedRotationalImpulse;
        }

        // Vehicle relative forward velocity
        private Vector3 VehicleForwardVelocity
        {
            get
            {
                return VehicleVelocity * Quaternion.Inverse(Quaternion.Normalize(VehicleFrameOrientation));
            }
        }

        private float VehicleForwardSpeed
        {
            get
            {
                return VehicleForwardVelocity.X;
            }
        }

        private Quaternion VehicleFrameOrientation
        {
            get
            {
                return VehicleOrientation * m_referenceFrame;
            }
        }

        #endregion // Known vehicle value functions

        // One step of the vehicle properties for the next 'pTimestep' seconds.
        internal void Step(float pTimestep)
        {
            if (!IsActive) return;

            ForgetKnownVehicleProperties();

            MoveLinear(pTimestep);
            MoveAngular(pTimestep);

            LimitRotation(pTimestep);

            // remember the position so next step we can limit absolute movement effects
            m_lastPositionVector = VehiclePosition;

            // If we forced the changing of some vehicle parameters, update the values and
            //      for the physics engine to note the changes so an UpdateProperties event will happen.
            PushKnownChanged();

            if (m_physicsScene.VehiclePhysicalLoggingEnabled)
                m_physicsScene.PE.DumpRigidBody(m_physicsScene.World, ControllingPrim.PhysBody);
        }

        // Called after the simulation step
        internal void PostStep(float pTimestep)
        {
            if (!IsActive) return;

            if (m_physicsScene.VehiclePhysicalLoggingEnabled)
                m_physicsScene.PE.DumpRigidBody(m_physicsScene.World, ControllingPrim.PhysBody);
        }

        // Apply the effect of the linear motor and other linear motions (like hover and float).
        private void MoveLinear(float pTimestep)
        {
            ComputeLinearVelocity(pTimestep);

            ComputeLinearTerrainHeightCorrection(pTimestep);

            ComputeLinearHover(pTimestep);

            ComputeLinearBlockingEndPoint(pTimestep);

            ComputeLinearMotorUp(pTimestep);

            ApplyGravity(pTimestep);

            ComputeLinearDeflection(pTimestep);

            // If not changing some axis, reduce out velocity
            if ((m_flags & (VehicleFlag.NO_X | VehicleFlag.NO_Y | VehicleFlag.NO_Z)) != 0)
            {
                Vector3 vel = VehicleVelocity;
                if ((m_flags & (VehicleFlag.NO_X)) != 0)
                {
                    vel.X = 0;
                }
                if ((m_flags & (VehicleFlag.NO_Y)) != 0)
                {
                    vel.Y = 0;
                }
                if ((m_flags & (VehicleFlag.NO_Z)) != 0)
                {
                    vel.Z = 0;
                }
                VehicleVelocity = vel;
            }

            // ==================================================================
            // Clamp high or low velocities
            float newVelocityLengthSq = VehicleVelocity.LengthSquared();
            if (newVelocityLengthSq > BSParam.VehicleMaxLinearVelocitySquared)
            {
                Vector3 origVelW = VehicleVelocity;         // DEBUG DEBUG
                VehicleVelocity /= VehicleVelocity.Length();
                VehicleVelocity *= BSParam.VehicleMaxLinearVelocity;
            }
            else if (newVelocityLengthSq < BSParam.VehicleMinLinearVelocitySquared)
            {
                Vector3 origVelW = VehicleVelocity;         // DEBUG DEBUG
                VehicleVelocity = Vector3.Zero;
            }

        } // end MoveLinear()

        public void ComputeLinearVelocity(float pTimestep)
        {
            // Step the motor from the current value. Get the correction needed this step.
            Vector3 currentVelV = VehicleForwardVelocity;
            if(m_linearMotorNewDirectionApply)
            {
                m_linearMotorNewDirectionApply = false;
                m_linearMotorDecayingDirection = m_linearMotorNewDirection;
            }
            else
            {
                m_linearMotorDecayingDirection -= (Vector3)m_linearMotorDecayingDirection * pTimestep / m_linearMotorDecayTimescale;
            }
            Vector3 linearMotorCorrectionV = (m_linearMotorDecayingDirection - currentVelV) * pTimestep / m_linearMotorTimescale;

            // Friction reduces vehicle motion based on absolute speed. Slow vehicle down by friction.
            Vector3 frictionFactorV = ComputeFrictionFactor(m_linearFrictionTimescale, pTimestep);
            linearMotorCorrectionV -= (currentVelV * frictionFactorV);

            // If we're a ground vehicle, don't add any upward Z movement on deflection
            Vector3 linearMotorVelocityW = linearMotorCorrectionV * VehicleFrameOrientation;

            // If we're a ground vehicle, don't add any upward Z movement on world coord
            if ((m_flags & VehicleFlag.LIMIT_MOTOR_UP) != 0)
            {
                if (linearMotorVelocityW.Z > 0f)
                    linearMotorVelocityW.Z = 0f;
            }

            // Add this correction to the velocity to make it faster/slower.
            VehicleVelocity += linearMotorVelocityW * VehicleFrameOrientation;
        }

        public static Vector3 ToAtAxis(Quaternion rot)
        {
            Vector3 vec;
            rot.Normalize(); // just in case
            vec.X = 2 * (rot.X * rot.X + rot.W * rot.W) - 1;
            vec.Y = 2 * (rot.X * rot.Y + rot.Z * rot.W);
            vec.Z = 2 * (rot.X * rot.Z - rot.Y * rot.W);
            return vec;
        }

        //Given a Deflection Effiency and a Velocity, Returns a Velocity that is Partially Deflected onto the X Axis
        //Clamped so that a DeflectionTimescale of less then 1 does not increase force over original velocity
        private void ComputeLinearDeflection(float pTimestep)
        {
            Vector3 linearDeflectionV = Vector3.Zero;
            Vector3 velocityV = VehicleForwardVelocity;

            if (BSParam.VehicleEnableLinearDeflection)
            {
                // Velocity in Y and Z dimensions is movement to the side or turning.
                // Compute deflection factor from the to the side and rotational velocity
                linearDeflectionV.Y = SortedClampInRange(0, (velocityV.Y * m_linearDeflectionEfficiency) / m_linearDeflectionTimescale, velocityV.Y);
                linearDeflectionV.X += Math.Abs(linearDeflectionV.Y);

                if((m_flags & VehicleFlag.NO_DEFLECTION_UP) == 0)
                {
                    linearDeflectionV.Z = SortedClampInRange(0, (velocityV.Z * m_linearDeflectionEfficiency) / m_linearDeflectionTimescale, velocityV.Z);
                    linearDeflectionV.X += Math.Abs(linearDeflectionV.Z);                
                }

                // Scale the deflection to the fractional simulation time
                linearDeflectionV *= pTimestep;

                // Subtract the sideways and rotational velocity deflection factors while adding the correction forward
                linearDeflectionV *= new Vector3(1, -1, -1);

                // Correction is vehicle relative. Convert to world coordinates.
                Vector3 linearDeflectionW = linearDeflectionV * VehicleFrameOrientation;

                // Optionally, if not colliding, don't effect world downward velocity. Let falling things fall.
                if (BSParam.VehicleLinearDeflectionNotCollidingNoZ && !m_controllingPrim.HasSomeCollision)
                {
                    linearDeflectionW.Z = 0f;
                }

                VehicleVelocity += linearDeflectionW;
            }
        }

        public void ComputeLinearTerrainHeightCorrection(float pTimestep)
        {
            // If below the terrain, move us above the ground a little.
            // TODO: Consider taking the rotated size of the object or possibly casting a ray.
            if (VehiclePosition.Z < GetTerrainHeight(VehiclePosition))
            {
                // Force position because applying force won't get the vehicle through the terrain
                Vector3 newPosition = VehiclePosition;
                newPosition.Z = GetTerrainHeight(VehiclePosition) + 1f;
                VehiclePosition = newPosition;
            }
        }

        public void ComputeLinearHover(float pTimestep)
        {
            // m_VhoverEfficiency: 0=bouncy, 1=totally damped
            // m_VhoverTimescale: time to achieve height
            if ((m_flags & (VehicleFlag.HOVER_WATER_ONLY | VehicleFlag.HOVER_TERRAIN_ONLY | VehicleFlag.HOVER_GLOBAL_HEIGHT)) != 0 && (m_VhoverHeight > 0) && (m_VhoverTimescale < 300))
            {
                // We should hover, get the target height
                if ((m_flags & VehicleFlag.HOVER_WATER_ONLY) != 0)
                {
                    m_VhoverTargetHeight = GetWaterLevel(VehiclePosition) + m_VhoverHeight;
                }
                if ((m_flags & VehicleFlag.HOVER_TERRAIN_ONLY) != 0)
                {
                    m_VhoverTargetHeight = GetTerrainHeight(VehiclePosition) + m_VhoverHeight;
                }
                if ((m_flags & VehicleFlag.HOVER_GLOBAL_HEIGHT) != 0)
                {
                    m_VhoverTargetHeight = m_VhoverHeight;
                }
                if ((m_flags & VehicleFlag.HOVER_UP_ONLY) != 0)
                {
                    // If body is already heigher, use its height as target height
                    if (VehiclePosition.Z > m_VhoverTargetHeight)
                    {
                        m_VhoverTargetHeight = VehiclePosition.Z;

                        // A 'misfeature' of this flag is that if the vehicle is above it's hover height,
                        //     the vehicle's buoyancy goes away. This is an SL bug that got used by so many
                        //    scripts that it could not be changed.
                        // So, if above the height, reapply gravity if buoyancy had it turned off.
                        if (m_VehicleBuoyancy != 0)
                        {
                            Vector3 appliedGravity = ControllingPrim.ComputeGravity(ControllingPrim.Buoyancy) * m_vehicleMass;
                            VehicleAddForce(appliedGravity);
                        }
                    }
                }

                if ((m_flags & VehicleFlag.LOCK_HOVER_HEIGHT) != 0)
                {
                    if (Math.Abs(VehiclePosition.Z - m_VhoverTargetHeight) > 0.2f)
                    {
                        Vector3 pos = VehiclePosition;
                        pos.Z = m_VhoverTargetHeight;
                        VehiclePosition = pos;
                    }
                }
                else
                {
                    // Error is positive if below the target and negative if above.
                    Vector3 hpos = VehiclePosition;
                    float verticalError = m_VhoverTargetHeight + 0.21728f - hpos.Z;
                    float verticalCorrection = verticalError / m_VhoverTimescale * pTimestep;
                    verticalCorrection *= m_VhoverEfficiency;
                    float verticalErrorScale = Math.Abs(verticalError * 10); /* downscale the last 0.1m */
                    if(verticalErrorScale < 1)
                    {
                        verticalCorrection *= verticalErrorScale;
                    }

                    if (verticalCorrection > 0 || (m_flags & VehicleFlag.HOVER_UP_ONLY) == 0)
                    {
                        hpos.Z += verticalCorrection;
                        VehiclePosition = hpos;
                    }

                    // Since we are hovering, we need to do the opposite of falling -- get rid of world Z
                    Vector3 vel = VehicleVelocity;
                    vel.Z = 0f;
                    VehicleVelocity = vel;

                    /*
                    float verticalCorrectionVelocity = verticalError / m_VhoverTimescale;
                    Vector3 verticalCorrection = new Vector3(0f, 0f, verticalCorrectionVelocity);
                    verticalCorrection *= m_vehicleMass;

                    // TODO: implement m_VhoverEfficiency correctly
                    VehicleAddForceImpulse(verticalCorrection);
                     */
                }
            }
        }

        public bool ComputeLinearBlockingEndPoint(float pTimestep)
        {
            bool changed = false;

            Vector3 pos = VehiclePosition;
            Vector3 posChange = pos - m_lastPositionVector;
            if (m_BlockingEndPoint != Vector3.Zero)
            {
                if (pos.X >= (m_BlockingEndPoint.X - (float)1))
                {
                    pos.X -= posChange.X + 1;
                    changed = true;
                }
                if (pos.Y >= (m_BlockingEndPoint.Y - (float)1))
                {
                    pos.Y -= posChange.Y + 1;
                    changed = true;
                }
                if (pos.Z >= (m_BlockingEndPoint.Z - (float)1))
                {
                    pos.Z -= posChange.Z + 1;
                    changed = true;
                }
                if (pos.X <= 0)
                {
                    pos.X += posChange.X + 1;
                    changed = true;
                }
                if (pos.Y <= 0)
                {
                    pos.Y += posChange.Y + 1;
                    changed = true;
                }
                if (changed)
                {
                    VehiclePosition = pos;
                }
            }
            return changed;
        }

        // From http://wiki.secondlife.com/wiki/LlSetVehicleFlags :
        //    Prevent ground vehicles from motoring into the sky. This flag has a subtle effect when
        //    used with conjunction with banking: the strength of the banking will decay when the
        //    vehicle no longer experiences collisions. The decay timescale is the same as
        //    VEHICLE_BANKING_TIMESCALE. This is to help prevent ground vehicles from steering
        //    when they are in mid jump.
        // TODO: this code is wrong. Also, what should it do for boats (height from water)?
        //    This is just using the ground and a general collision check. Should really be using
        //    a downward raycast to find what is below.
        public void ComputeLinearMotorUp(float pTimestep)
        {
            if ((m_flags & (VehicleFlag.LIMIT_MOTOR_UP)) != 0)
            {
                // This code tries to decide if the object is not on the ground and then pushing down
                /*
                float targetHeight = Type == Vehicle.TYPE_BOAT ? GetWaterLevel(VehiclePosition) : GetTerrainHeight(VehiclePosition);
                distanceAboveGround = VehiclePosition.Z - targetHeight;
                // Not colliding if the vehicle is off the ground
                if (!Prim.HasSomeCollision)
                {
                    // downForce = new Vector3(0, 0, -distanceAboveGround / m_bankingTimescale);
                    VehicleVelocity += new Vector3(0, 0, -distanceAboveGround);
                }
                // TODO: this calculation is wrong. From the description at
                //     (http://wiki.secondlife.com/wiki/Category:LSL_Vehicle), the downForce
                //     has a decay factor. This says this force should
                //     be computed with a motor.
                // TODO: add interaction with banking.
                VDetailLog("{0},  MoveLinear,limitMotorUp,distAbove={1},colliding={2},ret={3}",
                                Prim.LocalID, distanceAboveGround, Prim.HasSomeCollision, ret);
                 */

                // Another approach is to measure if we're going up. If going up and not colliding,
                //     the vehicle is in the air.  Fix that by pushing down.
                if (!ControllingPrim.HasSomeCollision && VehicleVelocity.Z > 0.1)
                {
                    // Get rid of any of the velocity vector that is pushing us up.
                    float upVelocity = VehicleVelocity.Z;
                    VehicleVelocity += new Vector3(0, 0, -upVelocity);

                    /*
                    // If we're pointed up into the air, we should nose down
                    Vector3 pointingDirection = Vector3.UnitX * VehicleFrameOrientation;
                    // The rotation around the Y axis is pitch up or down
                    if (pointingDirection.Y > 0.01f)
                    {
                        float angularCorrectionForce = -(float)Math.Asin(pointingDirection.Y);
                        Vector3 angularCorrectionVector = new Vector3(0f, angularCorrectionForce, 0f);
                        // Rotate into world coordinates and apply to vehicle
                        angularCorrectionVector *= VehicleFrameOrientation;
                        VehicleAddAngularForce(angularCorrectionVector);
                        VDetailLog("{0},  MoveLinear,limitMotorUp,newVel={1},pntDir={2},corrFrc={3},aCorr={4}",
                                    Prim.LocalID, VehicleVelocity, pointingDirection, angularCorrectionForce, angularCorrectionVector);
                    }
                        */
                }
            }
        }

        private void ApplyGravity(float pTimeStep)
        {
            Vector3 appliedGravity = m_VehicleGravity * m_vehicleMass;
            appliedGravity -= appliedGravity * m_VehicleBuoyancy;

            // Hack to reduce downward force if the vehicle is probably sitting on the ground
            if (ControllingPrim.HasSomeCollision && IsGroundVehicle)
                appliedGravity *= BSParam.VehicleGroundGravityFudge;

            VehicleAddForce(appliedGravity);
        }

        // =======================================================================
        // =======================================================================
        // Apply the effect of the angular motor.
        // The 'contribution' is how much angular correction velocity each function wants.
        //     All the contributions are added together and the resulting velocity is
        //     set directly on the vehicle.
        private void MoveAngular(float pTimestep)
        {
            ComputeAngularTurning(pTimestep);

            ComputeAngularVerticalAttraction(pTimestep);

            ComputeAngularDeflection(pTimestep);

            ComputeAngularBanking(pTimestep);
            // ==================================================================
            // ==================================================================
            if (VehicleRotationalVelocity.ApproxEquals(Vector3.Zero, 0.0001f))
            {
                // The vehicle is not adding anything angular wise.
                VehicleRotationalVelocity = Vector3.Zero;
            }

            // ==================================================================
            //Offset section
            if (m_linearMotorOffset != Vector3.Zero)
            {
                //Offset of linear velocity doesn't change the linear velocity,
                //   but causes a torque to be applied, for example...
                //
                //      IIIII     >>>   IIIII
                //      IIIII     >>>    IIIII
                //      IIIII     >>>     IIIII
                //          ^
                //          |  Applying a force at the arrow will cause the object to move forward, but also rotate
                //
                //
                // The torque created is the linear velocity crossed with the offset

                // TODO: this computation should be in the linear section
                //    because that is where we know the impulse being applied.
                Vector3 torqueFromOffset = Vector3.Zero;
                // torqueFromOffset = Vector3.Cross(m_linearMotorOffset, appliedImpulse);
                if (float.IsNaN(torqueFromOffset.X))
                    torqueFromOffset.X = 0;
                if (float.IsNaN(torqueFromOffset.Y))
                    torqueFromOffset.Y = 0;
                if (float.IsNaN(torqueFromOffset.Z))
                    torqueFromOffset.Z = 0;

                VehicleAddAngularForce(torqueFromOffset * m_vehicleMass);
            }

        }

        private void ComputeAngularTurning(float pTimestep)
        {
            // The user wants this many radians per second angular change?
            Vector3 origVehicleRotationalVelocity = VehicleRotationalVelocity;      // DEBUG DEBUG
            Vector3 currentAngularV = VehicleRotationalVelocity * Quaternion.Inverse(VehicleFrameOrientation);
            if(m_angularMotorNewDirectionApply)
            {
                m_angularMotorNewDirectionApply = false;
                m_angularMotorDecayingDirection = m_angularMotorNewDirection;
            }
            else
            {
                m_angularMotorDecayingDirection -= (Vector3)m_angularMotorDecayingDirection * pTimestep / m_angularMotorDecayTimescale;
            }
            Vector3 angularMotorContributionV = (m_angularMotorDecayingDirection - currentAngularV) * pTimestep / m_angularMotorTimescale; ;

            // Reduce any velocity by friction.
            Vector3 frictionFactorW = ComputeFrictionFactor(m_angularFrictionTimescale, pTimestep);
            angularMotorContributionV -= (currentAngularV * frictionFactorW);

            VehicleRotationalVelocity += angularMotorContributionV * VehicleFrameOrientation;
        }

        // From http://wiki.secondlife.com/wiki/Linden_Vehicle_Tutorial:
        //      Some vehicles, like boats, should always keep their up-side up. This can be done by
        //      enabling the "vertical attractor" behavior that springs the vehicle's local z-axis to
        //      the world z-axis (a.k.a. "up"). To take advantage of this feature you would set the
        //      VEHICLE_VERTICAL_ATTRACTION_TIMESCALE to control the period of the spring frequency,
        //      and then set the VEHICLE_VERTICAL_ATTRACTION_EFFICIENCY to control the damping. An
        //      efficiency of 0.0 will cause the spring to wobble around its equilibrium, while an
        //      efficiency of 1.0 will cause the spring to reach its equilibrium with exponential decay.
        public void ComputeAngularVerticalAttraction(float pTimestep)
        {

            // If vertical attaction timescale is reasonable
            if (BSParam.VehicleEnableAngularVerticalAttraction && m_verticalAttractionTimescale < m_verticalAttractionCutoff)
            {
                Vector3 currentEulerW = Vector3.Zero;
                Quaternion q = VehicleOrientation * Quaternion.Inverse(VehicleFrameOrientation);
                q.GetEulerAngles(out currentEulerW.X, out currentEulerW.Y, out currentEulerW.Z);
                currentEulerW.Z = 0;

                if((m_flags & VehicleFlag.LIMIT_ROLL_ONLY) != 0)
                {
                    currentEulerW.Y = 0;
                }
                currentEulerW *= m_verticalAttractionEfficiency * pTimestep / m_verticalAttractionTimescale;
                VehicleRotationalVelocity += currentEulerW;
            }
        }

        // Angular correction to correct the direction the vehicle is pointing to be
        //      the direction is should want to be pointing.
        // The vehicle is moving in some direction and correct its orientation to it is pointing
        //     in that direction.
        public void ComputeAngularDeflection(float pTimestep)
        {   
            if (BSParam.VehicleEnableAngularDeflection && m_angularDeflectionEfficiency != 0)
            {
                // The difference between what is and what should be.
                Vector3 movingDirection = VehicleForwardVelocity * Math.Sign(VehicleForwardVelocity.X);
                if(Math.Abs(VehicleForwardVelocity.X) < 0.001)
                {
                    movingDirection.X = 0.001f;
                    movingDirection.Y = VehicleForwardVelocity.Y;
                    movingDirection.Z = VehicleForwardVelocity.Z;
                }

                float feff = pTimestep * m_angularDeflectionEfficiency / m_angularDeflectionTimescale;
                Vector3 delta = Vector3.Zero;
                if(Math.Abs(movingDirection.Z) > 0.01)
                {
                    delta.Y = -(float)Math.Atan2(movingDirection.Z, movingDirection.X) * feff;
                }

                if(Math.Abs(movingDirection.Y) > 0.01)
                {
                    delta.Z = (float)Math.Atan2(movingDirection.Y, movingDirection.X) * feff;
                }

                VehicleRotationalVelocity += delta * VehicleFrameOrientation;
            }
        }

        // Angular change to rotate the vehicle around the Z axis when the vehicle
        //     is tipped around the X axis.
        // From http://wiki.secondlife.com/wiki/Linden_Vehicle_Tutorial:
        //      The vertical attractor feature must be enabled in order for the banking behavior to
        //      function. The way banking works is this: a rotation around the vehicle's roll-axis will
        //      produce a angular velocity around the yaw-axis, causing the vehicle to turn. The magnitude
        //      of the yaw effect will be proportional to the
        //          VEHICLE_BANKING_EFFICIENCY, the angle of the roll rotation, and sometimes the vehicle's
        //                 velocity along its preferred axis of motion.
        //          The VEHICLE_BANKING_EFFICIENCY can vary between -1 and +1. When it is positive then any
        //                  positive rotation (by the right-hand rule) about the roll-axis will effect a
        //                  (negative) torque around the yaw-axis, making it turn to the right--that is the
        //                  vehicle will lean into the turn, which is how real airplanes and motorcycle's work.
        //                  Negating the banking coefficient will make it so that the vehicle leans to the
        //                  outside of the turn (not very "physical" but might allow interesting vehicles so why not?).
        //           The VEHICLE_BANKING_MIX is a fake (i.e. non-physical) parameter that is useful for making
        //                  banking vehicles do what you want rather than what the laws of physics allow.
        //                  For example, consider a real motorcycle...it must be moving forward in order for
        //                  it to turn while banking, however video-game motorcycles are often configured
        //                  to turn in place when at a dead stop--because they are often easier to control
        //                  that way using the limited interface of the keyboard or game controller. The
        //                  VEHICLE_BANKING_MIX enables combinations of both realistic and non-realistic
        //                  banking by functioning as a slider between a banking that is correspondingly
        //                  totally static (0.0) and totally dynamic (1.0). By "static" we mean that the
        //                  banking effect depends only on the vehicle's rotation about its roll-axis compared
        //                  to "dynamic" where the banking is also proportional to its velocity along its
        //                  roll-axis. Finding the best value of the "mixture" will probably require trial and error.
        //      The time it takes for the banking behavior to defeat a preexisting angular velocity about the
        //      world z-axis is determined by the VEHICLE_BANKING_TIMESCALE. So if you want the vehicle to
        //      bank quickly then give it a banking timescale of about a second or less, otherwise you can
        //      make a sluggish vehicle by giving it a timescale of several seconds.
        public void ComputeAngularBanking(float pTimestep)
        {
            if (BSParam.VehicleEnableAngularBanking && m_bankingEfficiency != 0 && m_verticalAttractionTimescale < m_verticalAttractionCutoff)
            {
                Vector3 bankingContributionV = Vector3.Zero;

                // Figure out the yaw value for this much roll.
                float yawAngle = (VehicleRotationalVelocity * Quaternion.Inverse(VehicleFrameOrientation)).Z * m_bankingEfficiency;
                //        actual error  =       static turn error            +           dynamic turn error
                float mixedYawAngle = (yawAngle * (1f - m_bankingMix)) + ((yawAngle * m_bankingMix) * Math.Abs(VehicleForwardSpeed));

                // TODO: the banking effect should not go to infinity but what to limit it to?
                //     And what should happen when this is being added to a user defined yaw that is already PI*4?
                mixedYawAngle = ClampInRange(-12, mixedYawAngle, 12);

                // Build the force vector to change rotation from what it is to what it should be
                bankingContributionV.X = -mixedYawAngle;

                bankingContributionV = bankingContributionV * pTimestep / m_bankingTimescale;

                VehicleRotationalVelocity += bankingContributionV * VehicleFrameOrientation;
            }
        }

        // This is from previous instantiations of XXXDynamics.cs.
        // Applies roll reference frame.
        // TODO: is this the right way to separate the code to do this operation?
        //    Should this be in MoveAngular()?
        internal void LimitRotation(float timestep)
        {
            Quaternion rotq = VehicleOrientation;
            Quaternion m_rot = rotq;
            Quaternion rollRef = m_RollreferenceFrame;
            if (rollRef != Quaternion.Identity)
            {
                if (rotq.X >= rollRef.X)
                {
                    m_rot.X = rotq.X - (rollRef.X / 2);
                }
                if (rotq.Y >= rollRef.Y)
                {
                    m_rot.Y = rotq.Y - (rollRef.Y / 2);
                }
                if (rotq.X <= -rollRef.X)
                {
                    m_rot.X = rotq.X + (rollRef.X / 2);
                }
                if (rotq.Y <= -rollRef.Y)
                {
                    m_rot.Y = rotq.Y + (rollRef.Y / 2);
                }
            }
            if ((m_flags & VehicleFlag.LOCK_ROTATION) != 0)
            {
                m_rot.X = 0;
                m_rot.Y = 0;
            }
            if (rotq != m_rot)
            {
                VehicleOrientation = m_rot;
            }

        }

        // Given a friction vector (reduction in seconds) and a timestep, return the factor to reduce
        //     some value by to apply this friction.
        private Vector3 ComputeFrictionFactor(Vector3 friction, float pTimestep)
        {
            Vector3 frictionFactor = Vector3.Zero;
            if (friction != BSMotor.InfiniteVector)
            {
                // frictionFactor = (Vector3.One / FrictionTimescale) * timeStep;
                // Individual friction components can be 'infinite' so compute each separately.
                frictionFactor.X = (friction.X == BSMotor.Infinite) ? 0f : (1f / friction.X);
                frictionFactor.Y = (friction.Y == BSMotor.Infinite) ? 0f : (1f / friction.Y);
                frictionFactor.Z = (friction.Z == BSMotor.Infinite) ? 0f : (1f / friction.Z);
                frictionFactor *= pTimestep;
            }
            return frictionFactor;
        }

        private float SortedClampInRange(float clampa, float val, float clampb)
        {
            if (clampa > clampb)
            {
                float temp = clampa;
                clampa = clampb;
                clampb = temp;
            }
           return ClampInRange(clampa, val, clampb);

        }

        private float ClampInRange(float low, float val, float high)
        {
            return Math.Max(low, Math.Min(val, high));
            // return Utils.Clamp(val, low, high);
        }
    }
}
