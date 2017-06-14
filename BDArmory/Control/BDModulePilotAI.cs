using System;
using System.Collections.Generic;
using BDArmory.Misc;
using BDArmory.Parts;
using BDArmory.UI;
using UnityEngine;

namespace BDArmory.Control
{
	public class BDModulePilotAI : PartModule
	{
		public enum SteerModes{NormalFlight, Aiming}
		SteerModes steerMode = SteerModes.NormalFlight;

		public enum PilotCommands{Free, Attack, Follow, FlyTo}


		[KSPField(isPersistant = true)]
		public bool pilotEnabled = false;

		bool belowMinAltitude = false;
		bool extending = false;

		bool requestedExtend = false;
		Vector3 requestedExtendTpos;

		public bool IsExtending
		{
			get { return extending || requestedExtend; }
		}

		public bool isLeadingFormation = false;

		public void RequestExtend(Vector3 tPosition)
		{
			requestedExtend = true;
			requestedExtendTpos = tPosition;
		}

		GameObject vobj;
		Transform velocityTransform
		{
			get
			{
				if(!vobj)
				{
					vobj = new GameObject("velObject");
					vobj.transform.position = vessel.ReferenceTransform.position;
					vobj.transform.parent = vessel.ReferenceTransform;
				}

				return vobj.transform;
			}
		}


		Vessel targetVessel;


		Transform vesselTransform;

		Vector3 upDirection = Vector3.up;

		public MissileFire weaponManager;


		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Default Alt."),
			UI_FloatRange(minValue = 500f, maxValue = 8500f, stepIncrement = 25f, scene = UI_Scene.All)]
		public float defaultAltitude = 1500;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Min Altitude"),
			UI_FloatRange(minValue = 150f, maxValue = 6000, stepIncrement = 50f, scene = UI_Scene.All)]
		public float minAltitude = 500f;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steer Factor"),
			UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
		public float steerMult = 6;
		//make a combat steer mult and idle steer mult

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Pitch Ki"),
			UI_FloatRange(minValue = 0f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
		public float pitchKiAdjust = 5;


		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steer Limiter"),
			UI_FloatRange(minValue = .1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
		public float maxSteer = 1;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steer Damping"),
			UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.5f, scene = UI_Scene.All)]
		public float steerDamping = 3;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Max Speed"),
			UI_FloatRange(minValue = 20f, maxValue = 800f, stepIncrement = 1.0f, scene = UI_Scene.All)]
		public float maxSpeed = 325;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "TakeOff Speed"),
			UI_FloatRange(minValue = 10f, maxValue = 200f, stepIncrement = 1.0f, scene = UI_Scene.All)]
		public float takeOffSpeed = 70;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "MinCombatSpeed"),
			UI_FloatRange(minValue = 20f, maxValue = 200, stepIncrement = 1.0f, scene = UI_Scene.All)]
		public float minSpeed = 60f;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Idle Speed"),
			UI_FloatRange(minValue = 10f, maxValue = 200f, stepIncrement = 1.0f, scene = UI_Scene.All)]
		public float idleSpeed = 120f;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Max G"),
			UI_FloatRange(minValue = 2f, maxValue = 25f, stepIncrement = 0.25f, scene = UI_Scene.All)]
		public float maxAllowedGForce = 10;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Max AoA"),
			UI_FloatRange(minValue = 0f, maxValue = 85f, stepIncrement = 2.5f, scene = UI_Scene.All)]
		public float maxAllowedAoA = 35;
		float maxAllowedCosAoA = 0;
		float lastAllowedAoA = 0;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Standby Mode"),
			UI_Toggle(enabledText = "On", disabledText = "Off")]
		public bool standbyMode = false;

		//manueuverability and g loading data
		float maxDynPresGRecorded = 0;

		float maxPosG = 0;
		float cosAoAAtMaxPosG = 0;

		float maxNegG = 0;
		float cosAoAAtMaxNegG = 0;

		float[] gLoadMovingAvgArray = new float[32];
		float[] cosAoAMovingAvgArray = new float[32];
		int movingAvgIndex = 0;

		float gLoadMovingAvg = 0;
		float cosAoAMovingAvg = 0;

		float gaoASlopePerDynPres = 0;        //used to limit control input at very high dynamic pressures to avoid structural failure
		float gOffsetPerDynPres = 0;

		float posPitchDynPresLimitIntegrator = 1;
		float negPitchDynPresLimitIntegrator = -1;

		float lastCosAoA = 0;
		float lastPitchInput = 0;

		//Controller Integral
		float pitchIntegral = 0;

		//instantaneous turn radius and possible acceleration from lift
		//properties can be used so that other AI modules can read this for future maneuverability comparisons between craft
		float turnRadius;
		public float TurnRadius
		{
			get { return turnRadius; }
			private set { turnRadius = value; }
		}

		float maxLiftAcceleration;
		public float MaxLiftAcceleration
		{
			get { return maxLiftAcceleration; }
			private set { maxLiftAcceleration = value; }
		}


		float turningTimer = 0;
		float evasiveTimer = 0;
		Vector3 lastTargetPosition;

		string debugString = string.Empty;

		LineRenderer lr;
		Vector3 flyingToPosition;

		public Vector3d defaultOrbitCoords;

		//speed controller
		BDAirspeedControl speedController;
		bool useAB = true;
		bool useBrakes = true;
		bool regainEnergy = false;

		//collision detection
		int collisionDetectionTicker = 0;
		float collisionDetectionTimer = 0;
		Vector3 collisionAvoidDirection;

		//wing command
		int commandFollowIndex = 0;
		PilotCommands command;
		public PilotCommands currentCommand
		{
			get
			{
				return command;
			}
		}
		public ModuleWingCommander commandLeader;
		bool useRollHint = false;
		Vector3d commandGeoPos;
		public Vector3d commandPosition
		{
			get
			{
				return VectorUtils.GetWorldSurfacePostion(commandGeoPos, vessel.mainBody);
			}
			set
			{
				commandGeoPos = VectorUtils.WorldPositionToGeoCoords(value, vessel.mainBody);
			}
		}
		public Vector3d commandGPS
		{
			get
			{
				return commandGeoPos;
			}
		}
		double commandSpeed;
		Vector3d commandHeading;
		public string currentStatus = "Free";


		void Start()
		{
			if(HighLogic.LoadedSceneIsFlight)
			{
				part.OnJustAboutToBeDestroyed += DeactivatePilot;
				vessel.OnJustAboutToBeDestroyed += DeactivatePilot;
				MissileFire.OnToggleTeam += OnToggleTeam;
				vesselTransform = vessel.ReferenceTransform;

			    List<MissileFire>.Enumerator wms = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
                while (wms.MoveNext())
				{
					weaponManager = wms.Current;
					break;
				}

				if(pilotEnabled)
				{
					ActivatePilot();
				}
				maxAllowedCosAoA = (float)Math.Cos(maxAllowedAoA * Math.PI / 180.0);
				lastAllowedAoA = maxAllowedAoA;
			}

			RefreshPartWindow();
		}

		void OnDestroy()
		{
			MissileFire.OnToggleTeam -= OnToggleTeam;
		}

		void OnToggleTeam(MissileFire mf, BDArmorySettings.BDATeams team)
		{
			if(mf.vessel == vessel || (commandLeader && commandLeader.vessel == mf.vessel))
			{
				ReleaseCommand();
			}
		}

		[KSPAction("Activate Pilot")]
		public void AGActivatePilot(KSPActionParam param)
		{
			ActivatePilot();
		}

		[KSPAction("Deactivate Pilot")]
		public void AGDeactivatePilot(KSPActionParam param)
		{
			DeactivatePilot();
		}

		[KSPAction("Toggle Pilot")]
		public void AGTogglePilot(KSPActionParam param)
		{
			TogglePilot();
		}


		public void ActivatePilot()
		{
			pilotEnabled = true;
			vessel.OnFlyByWire -= AutoPilot;
			vessel.OnFlyByWire += AutoPilot;
			belowMinAltitude = vessel.LandedOrSplashed;

			prevTargetDir = vesselTransform.up;

			if(!speedController)
			{
				speedController = gameObject.AddComponent<BDAirspeedControl>();
				speedController.vessel = vessel;
			}

			speedController.Activate();

			GameEvents.onVesselDestroy.Remove(RemoveAutopilot);
			GameEvents.onVesselDestroy.Add(RemoveAutopilot);

			defaultOrbitCoords = VectorUtils.WorldPositionToGeoCoords(vessel.ReferenceTransform.position, vessel.mainBody);

			RefreshPartWindow();
		}

		public void DeactivatePilot()
		{
			pilotEnabled = false;
			vessel.OnFlyByWire -= AutoPilot;
			RefreshPartWindow();

			if(speedController)
			{
				speedController.Deactivate();
			}
		}

		void RemoveAutopilot(Vessel v)
		{
			if(v == vessel)
			{
				v.OnFlyByWire -= AutoPilot;
			}
		}



		[KSPEvent(guiActive = true, guiName = "Toggle Pilot", active = true)]
		public void TogglePilot()
		{
			if(pilotEnabled)
			{
				DeactivatePilot();
			}
			else
			{
				ActivatePilot();
			}
		}

		void RefreshPartWindow()
		{
			Events["TogglePilot"].guiName = pilotEnabled ? "Deactivate Pilot" : "Activate Pilot";

			//Misc.RefreshAssociatedWindows(part);
		}

		void Update()
		{
			if(BDArmorySettings.DRAW_DEBUG_LINES && pilotEnabled)
			{
				if(lr)
				{
					lr.enabled = true;
					lr.SetPosition(0, vessel.ReferenceTransform.position);
					lr.SetPosition(1, flyingToPosition);
				}
				else
				{
					lr = gameObject.AddComponent<LineRenderer>();
					lr.SetVertexCount(2);
					lr.SetWidth(0.5f, 0.5f);
				}


				minSpeed = Mathf.Clamp(minSpeed, 0, idleSpeed - 20);
				minSpeed = Mathf.Clamp(minSpeed, 0, maxSpeed - 20);
			}
			else
			{
				if(lr)
				{
					lr.enabled = false;
				}
			}
		}



		float finalMaxSteer = 1;
		void AutoPilot(FlightCtrlState s)
		{
			if(!vessel || !vessel.transform || vessel.packed || !vessel.mainBody)
			{
				return;
			}
			vesselTransform = vessel.ReferenceTransform;
			finalMaxSteer = maxSteer;

			//default brakes off full throttle
			//s.mainThrottle = 1;

			//vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
			AdjustThrottle(maxSpeed, true);
			useAB = true;
			useBrakes = true;
			vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

			steerMode = SteerModes.NormalFlight;
			useVelRollTarget = false;



			GetGuardTarget();
			if(vessel.LandedOrSplashed && standbyMode && weaponManager && (BDATargetManager.TargetDatabase[BDATargetManager.BoolToTeam(weaponManager.team)].Count == 0||BDArmorySettings.PEACE_MODE))
			{
				//s.mainThrottle = 0;
				//vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
				AdjustThrottle(0, true);
				return;
			}
			//upDirection = -FlightGlobals.getGeeForceAtPosition(transform.position).normalized;
			upDirection = VectorUtils.GetUpDirection(vessel.transform.position);
			debugString = string.Empty;

			CalculateAccelerationAndTurningCircle();
			float minAltNeeded = MinAltitudeNeeded();
			debugString += "minAltNeeded: " + minAltNeeded;
			if (MissileGuidance.GetRadarAltitude(vessel) < minAltNeeded)
			{
				belowMinAltitude = true;
			}

			if(vessel.srfSpeed < minSpeed)
			{
				regainEnergy = true;
			}
			else if(!belowMinAltitude && vessel.srfSpeed > Mathf.Min(minSpeed + 20f, idleSpeed))
			{
				regainEnergy = false;
			}



			if (belowMinAltitude)
			{
				if(command != PilotCommands.Follow)
				{
					currentStatus = "Gain Alt.";
				}
				TakeOff(s);
				turningTimer = 0;
			}
			else
			{
				if(FlyAvoidCollision(s))
				{
					turningTimer = 0;
				}
				else if(command != PilotCommands.Free)
				{
					UpdateCommand(s);
				}
				else
				{
					UpdateAI(s);
				}
			}
			UpdateGAndAoALimits(s);
			AdjustPitchForGAndAoALimits(s);

		}

		void UpdateAI(FlightCtrlState s)
		{
			currentStatus = "Free";
			if(weaponManager && weaponManager.guardMode && !targetVessel)
			{
				TargetInfo potentialTarget = BDATargetManager.GetLeastEngagedTarget(weaponManager);
				if(potentialTarget && potentialTarget.Vessel)
				{
					targetVessel = potentialTarget.Vessel;
				}
			}

			if(requestedExtend)
			{
				requestedExtend = false;
				extending = true;
				lastTargetPosition = requestedExtendTpos;
			}

			if(evasiveTimer > 0 || (weaponManager && (weaponManager.missileIsIncoming || weaponManager.isChaffing || weaponManager.isFlaring || weaponManager.underFire)))
			{
				if(evasiveTimer < 1)
				{
					threatRelativePosition = vessel.srf_velocity.normalized + vesselTransform.right;

					if(weaponManager)
					{
						if(weaponManager.rwr.rwrEnabled) //use rwr to check missile threat direction
						{
							Vector3 missileThreat = Vector3.zero;
							bool missileThreatDetected = false;
							float closestMissileThreat = float.MaxValue;
							for(int i = 0; i < weaponManager.rwr.pingsData.Length; i++)
							{
								TargetSignatureData threat = weaponManager.rwr.pingsData[i];
								if(threat.exists && threat.signalStrength == 4)
								{
									missileThreatDetected = true;
									float dist = Vector3.Distance(weaponManager.rwr.pingWorldPositions[i], vesselTransform.position);
									if(dist < closestMissileThreat)
									{
										closestMissileThreat = dist;
										missileThreat = weaponManager.rwr.pingWorldPositions[i];
									}
								}
							}
							if(missileThreatDetected)
							{
								threatRelativePosition = missileThreat - vesselTransform.position;
							}
						}

						if(weaponManager.underFire)
						{
							threatRelativePosition = weaponManager.incomingThreatPosition - vesselTransform.position;
						}
					}
				}
				Evasive(s);
				evasiveTimer += Time.fixedDeltaTime;
				turningTimer = 0;

				if(evasiveTimer > 3)
				{
					evasiveTimer = 0;
					collisionDetectionTicker = 21; //check for collision again after exiting evasion routine
				}
			}
			else if(!extending && weaponManager && targetVessel != null && targetVessel.transform != null)
			{
				evasiveTimer = 0;
				if(!targetVessel.LandedOrSplashed)
				{
					Vector3 targetVesselRelPos = targetVessel.vesselTransform.position - vesselTransform.position;
					if (vessel.altitude < defaultAltitude && Vector3.Angle(targetVesselRelPos, -upDirection) < 35)
					{
						//dangerous if low altitude and target is far below you - don't dive into ground!
						extending = true;
						lastTargetPosition = targetVessel.vesselTransform.position;
					}

					if (Vector3.Angle(targetVessel.vesselTransform.position - vesselTransform.position, vesselTransform.up) > 35)
					{
						turningTimer += Time.deltaTime;
					}
					else
					{
						turningTimer = 0;
					}

					debugString += "\nturningTimer: " + turningTimer;

					float targetForwardDot = Vector3.Dot(targetVesselRelPos.normalized, vesselTransform.up);
					float targetVelFrac = (float)(targetVessel.srfSpeed / vessel.srfSpeed);      //this is the ratio of the target vessel's velocity to this vessel's srfSpeed in the forward direction; this allows smart decisions about when to break off the attack

					if (targetVelFrac < 0.8f && targetForwardDot < 0.2f && targetVesselRelPos.magnitude < 400)
					{
						extending = true;
						lastTargetPosition = targetVessel.vesselTransform.position - vessel.srf_velocity;       //we'll set our last target pos based on the enemy vessel and where we were 1 seconds ago
						weaponManager.ForceScan();
					}
					if(turningTimer > 15)
					{
						//extend if turning circles for too long
						//extending = true;
						RequestExtend(targetVessel.vesselTransform.position);
						turningTimer = 0;
						weaponManager.ForceScan();
						//lastTargetPosition = targetVessel.transform.position;
					}
				}
				else //extend if too close for agm attack
				{
					float extendDistance = Mathf.Clamp(weaponManager.guardRange - 1800, 2500, 4000);
					float srfDist = Vector3.Distance(GetSurfacePosition(targetVessel.transform.position), GetSurfacePosition(vessel.transform.position));

					if(srfDist < extendDistance && Vector3.Angle(vesselTransform.up, targetVessel.transform.position - vessel.transform.position) > 45)
					{
						extending = true;
						lastTargetPosition = targetVessel.transform.position;
						weaponManager.ForceScan();
					}
				}


				if(!extending)
				{
					currentStatus = "Engaging";
					debugString += "\nFlying to target";
					FlyToTargetVessel(s, targetVessel);
				}
			}
			else
			{
				evasiveTimer = 0;
				if(!extending)
				{
					currentStatus = "Orbiting";
					FlyOrbit(s, defaultOrbitCoords, 2000, idleSpeed, true);
				}
			}

			if(extending)
			{
				evasiveTimer = 0;
				currentStatus = "Extending";
				debugString += "\nExtending";
				FlyExtend(s, lastTargetPosition);
			}
		}


		bool FlyAvoidCollision(FlightCtrlState s)
		{
			if(collisionDetectionTimer > 2)
			{
				collisionDetectionTimer = 0;
				collisionDetectionTicker = 20;
			}
			if(collisionDetectionTimer > 0)
			{
				//fly avoid
				currentStatus = "AvoidCollision";
				debugString += "\nAvoiding Collision";
				collisionDetectionTimer += Time.fixedDeltaTime;


				Vector3 target = vesselTransform.position + collisionAvoidDirection;
				FlyToPosition(s, target);
				return true;
			}
			else if(collisionDetectionTicker > 20)
			{
				collisionDetectionTicker = 0;
				bool avoid = false;
				Vector3 badDirection;
				if(DetectCollision(flyingToPosition - vesselTransform.position, out badDirection))
				{
					avoid = true;
				}
				else if(command != PilotCommands.Follow && !isLeadingFormation) //check collisions with other flying vessels
				{
				    List<Vessel>.Enumerator vs = BDATargetManager.LoadedVessels.GetEnumerator();
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null) continue;
                        if (vs.Current == vessel || vs.Current.Landed ||
                            !(Vector3.Dot(vs.Current.transform.position - vesselTransform.position,
                                  vesselTransform.up) > 0)) continue;
                        if (!PredictCollisionWithVessel(vs.Current, 2.5f, 0.5f, out badDirection)) continue;
                        avoid = true;
                        break;
                    }
				}

			    if (!avoid) return false;
			    collisionDetectionTimer += Time.fixedDeltaTime;
			    Vector3 axis = -Vector3.Cross(vesselTransform.up, badDirection);
			    collisionAvoidDirection = Quaternion.AngleAxis(25, axis) * badDirection;        //don't need to change the angle that much to avoid, and it should prevent stupid suicidal manuevers as well

			    FlyAvoidCollision(s);
			    return true;
			}
			else
			{
				collisionDetectionTicker++;
			}

			return false;
		}

		bool PredictCollisionWithVessel(Vessel v, float maxTime, float interval, out Vector3 badDirection)
		{
			if(v == weaponManager.incomingMissileVessel) //evasive will handle avoiding missiles
			{
				badDirection = Vector3.zero;
				return false;
			}

			float time = Mathf.Min(0.5f, maxTime);
			while(time < maxTime)
			{
				Vector3 tPos = PredictPosition(v, time);
				Vector3 myPos = PredictPosition(vessel, time);
				if(Vector3.SqrMagnitude(tPos - myPos) < 900f)
				{
					badDirection = tPos - vesselTransform.position;
					return true;
				}

				time = Mathf.MoveTowards(time, maxTime, interval);
			}

			badDirection = Vector3.zero;
			return false;
		}

		Vector3 PredictPosition(Vessel v, float time)
		{
			Vector3 pos = v.CoM;
			pos += v.srf_velocity * time;
			pos += 0.5f * v.acceleration * time * time;
			return pos;
		}

		void FlyToTargetVessel(FlightCtrlState s, Vessel v)
		{
			Vector3 target = v.CoM;
			MissileBase missile = null;
			Vector3 vectorToTarget = v.transform.position - vesselTransform.position;
			float distanceToTarget = vectorToTarget.magnitude;
			float planarDistanceToTarget = Vector3.ProjectOnPlane(vectorToTarget, upDirection).magnitude;
			float angleToTarget = Vector3.Angle(target - vesselTransform.position, vesselTransform.up);
			if(weaponManager)
			{
				missile = weaponManager.CurrentMissile;
				if(missile != null)
				{
					if(missile.GetWeaponClass() == WeaponClasses.Missile)
					{
						if(distanceToTarget > 5500f)
						{
							finalMaxSteer = GetSteerLimiterForSpeedAndPower();
						}

						if(missile.TargetingMode == MissileLauncher.TargetingModes.Heat && !weaponManager.heatTarget.exists)
						{
							debugString += "\nAttempting heat lock";
							target += v.srf_velocity.normalized * 10;
						}
						else
						{
							target = MissileGuidance.GetAirToAirFireSolution(missile, v);
						}

						if(angleToTarget < 20f)
						{
							steerMode = SteerModes.Aiming;
						}
					}
					else //bombing
					{
						if(distanceToTarget > 4500f)
						{
							finalMaxSteer = GetSteerLimiterForSpeedAndPower();
						}

						if(angleToTarget < 45f)
						{
							/*
							target = GetSurfacePosition(target) + (vessel.upAxis * vessel.altitude);
							Vector3 fixedTDir = Quaternion.FromToRotation(Vector3.ProjectOnPlane(vessel.srf_velocity, vessel.upAxis), target - vesselTransform.position) * (target - vesselTransform.position);
							target = FlightPosition(vesselTransform.position + fixedTDir, Mathf.Max(defaultAltitude - 500f, minAltitude));
							*/

							target = target + (Mathf.Max(defaultAltitude - 500f, minAltitude) * upDirection);
							Vector3 tDir = (target - vesselTransform.position).normalized;
							tDir = (1000 * tDir) - (vessel.srf_velocity.normalized * 600);
							target = vesselTransform.position + tDir;

						}
						else
						{
							target = target + (Mathf.Max(defaultAltitude - 500f, minAltitude) * upDirection);
						}
					}
				}
				else if(weaponManager.currentGun)
				{
					ModuleWeapon weapon = weaponManager.currentGun;
					if(weapon != null)
					{
						Vector3 leadOffset = weapon.GetLeadOffset();

						float targetAngVel = Vector3.Angle(v.transform.position - vessel.transform.position, v.transform.position + (vessel.srf_velocity) - vessel.transform.position);
						debugString += "\ntargetAngVel: " + targetAngVel;
						float magnifier = Mathf.Clamp(targetAngVel, 1f, 2f);
						magnifier += ((magnifier-1f) * Mathf.Sin(Time.time *0.75f));
						target -= magnifier * leadOffset;

						angleToTarget = Vector3.Angle(vesselTransform.up, target - vesselTransform.position);
						if(distanceToTarget < weaponManager.gunRange && angleToTarget < 20)
						{
							steerMode = SteerModes.Aiming; //steer to aim
						}
						else
						{
							if(distanceToTarget > 3500f || vessel.srfSpeed < takeOffSpeed)
							{
								finalMaxSteer = GetSteerLimiterForSpeedAndPower();
							}
							else
							{
								//figuring how much to lead the target's movement to get there after its movement assuming we can manage a constant speed turn
								//this only runs if we're not aiming and not that far from the target
								float curVesselMaxAccel = Math.Min(maxDynPresGRecorded * (float)vessel.dynamicPressurekPa, maxAllowedGForce * 9.81f);
								if (curVesselMaxAccel > 0)
								{
									float timeToTurn = (float)vessel.srfSpeed * angleToTarget * Mathf.Deg2Rad / curVesselMaxAccel;
									target += v.srf_velocity * timeToTurn;
									//target += 0.5f * v.acceleration * timeToTurn * timeToTurn;
								}
							}
						}

						if(v.LandedOrSplashed)
						{
							if(distanceToTarget > defaultAltitude * 2.2f)
							{
								target = FlightPosition(target, defaultAltitude);
							}
							else
							{
								steerMode = SteerModes.Aiming;
							}
						}
						else if(distanceToTarget > weaponManager.gunRange * 1.5f || Vector3.Dot(target - vesselTransform.position, vesselTransform.up) < 0)
						{
							target = v.CoM;
						}
					}
				}
				else if(planarDistanceToTarget > weaponManager.gunRange * 1.25f && (vessel.altitude < targetVessel.altitude || MissileGuidance.GetRadarAltitude(vessel) < defaultAltitude)) //climb to target vessel's altitude if lower and still too far for guns
				{
					finalMaxSteer = GetSteerLimiterForSpeedAndPower();
					target = vesselTransform.position + GetLimitedClimbDirectionForSpeed(vectorToTarget);
				}
				else
				{
					finalMaxSteer = GetSteerLimiterForSpeedAndPower();
				}
			}


			float targetDot = Vector3.Dot(vesselTransform.up, v.transform.position-vessel.transform.position);

			//manage speed when close to enemy
			float finalMaxSpeed = maxSpeed;
			if(targetDot > 0)
			{
				finalMaxSpeed = Mathf.Max((distanceToTarget - 100) / 8, 0) + (float)v.srfSpeed;
				finalMaxSpeed = Mathf.Max(finalMaxSpeed, minSpeed+25f);
			}
			AdjustThrottle(finalMaxSpeed, true);

			if((targetDot < 0 && vessel.srfSpeed > finalMaxSpeed)
				&& distanceToTarget < 300 && vessel.srfSpeed < v.srfSpeed * 1.25f && Vector3.Dot(vessel.srf_velocity, v.srf_velocity) > 0) //distance is less than 800m
			{
				debugString += ("\nEnemy on tail. Braking");
				AdjustThrottle(minSpeed, true);
			}
			if(missile!=null 
				&& targetDot > 0
				&& distanceToTarget < MissileLaunchParams.GetDynamicLaunchParams(missile, v.srf_velocity, v.transform.position).minLaunchRange
				&& vessel.srfSpeed > idleSpeed)
			{
				//extending = true;
				//lastTargetPosition = v.transform.position;
				RequestExtend(lastTargetPosition);
			}

			if(regainEnergy && angleToTarget > 30f)
			{
				RegainEnergy(s, target - vesselTransform.position);
				return;
			}
			else
			{
				useVelRollTarget = true;
				FlyToPosition(s, target);
				return;
			}
		}



		void RegainEnergy(FlightCtrlState s, Vector3 direction)
		{
			debugString += "\nRegaining energy";
			steerMode = SteerModes.Aiming;
			Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDirection);
			float angle = (Mathf.Clamp(MissileGuidance.GetRadarAltitude(vessel) - minAltitude, 0, 1500) / 1500) * 90;
			angle = Mathf.Clamp(angle, 0, 55) * Mathf.Deg2Rad;
			Vector3 targetDirection = Vector3.RotateTowards(planarDirection, -upDirection, angle, 0);
			targetDirection = Vector3.RotateTowards(vessel.srf_velocity, targetDirection, 15f * Mathf.Deg2Rad, 0).normalized;

			AdjustThrottle(maxSpeed, false);
			FlyToPosition(s, vesselTransform.position + (targetDirection*100));
		}

		float GetSteerLimiterForSpeedAndPower()
		{
			float possibleAccel = speedController.GetPossibleAccel();
			float speed = (float)vessel.srfSpeed;
			debugString += "\npossibleAccel: " + possibleAccel;
			float limiter = ((speed-50) / 330f) + possibleAccel / 15f;
			debugString += "\nunclamped limiter: " + limiter;
			return Mathf.Clamp01(limiter);
		}

		//test
		Vector3 prevTargetDir;
		bool useVelRollTarget = false;
		void FlyToPosition(FlightCtrlState s, Vector3 targetPosition)
		{
			if(!belowMinAltitude)
			{
				if(weaponManager && Time.time - weaponManager.timeBombReleased < 1.5f)
				{
					targetPosition = vessel.transform.position + vessel.srf_velocity;
				}

				targetPosition = FlightPosition(targetPosition, minAltitude);
				targetPosition = vesselTransform.position + ((targetPosition - vesselTransform.position).normalized * 100);
			}

			Vector3d srfVel = vessel.srf_velocity;
			if(srfVel != Vector3d.zero)
			{
				velocityTransform.rotation = Quaternion.LookRotation(srfVel, -vesselTransform.forward);
			}
			velocityTransform.rotation = Quaternion.AngleAxis(90, velocityTransform.right) * velocityTransform.rotation;

			//ang vel 
			Vector3 localAngVel = vessel.angularVelocity;
			//test
			Vector3 currTargetDir = (targetPosition-vesselTransform.position).normalized;
			if(steerMode == SteerModes.NormalFlight)
			{
				float gRotVel = ((10f * maxAllowedGForce) / ((float)vessel.srfSpeed));
				//currTargetDir = Vector3.RotateTowards(prevTargetDir, currTargetDir, gRotVel*Mathf.Deg2Rad, 0);
			}
			Vector3 targetAngVel = Vector3.Cross(prevTargetDir, currTargetDir)/Time.fixedDeltaTime;
			Vector3 localTargetAngVel = vesselTransform.InverseTransformVector(targetAngVel);
			prevTargetDir = currTargetDir;
			targetPosition = vessel.transform.position + (currTargetDir * 100);


			if(BDArmorySettings.DRAW_DEBUG_LINES)
			{
				flyingToPosition = targetPosition;
			}

			//test poststall
			float AoA = Vector3.Angle(vessel.ReferenceTransform.up, vessel.srf_velocity);
			if(AoA > 30f)
			{
				steerMode = SteerModes.Aiming;
			}

			//slow down for tighter turns
			float velAngleToTarget = Vector3.Angle(targetPosition-vesselTransform.position, vessel.srf_velocity);
			float normVelAngleToTarget = Mathf.Clamp(velAngleToTarget, 0, 90)/90;
			float speedReductionFactor = 1.25f;
			float finalSpeed = Mathf.Min(speedController.targetSpeed, Mathf.Clamp(maxSpeed - (speedReductionFactor * normVelAngleToTarget), idleSpeed, maxSpeed));
			debugString += "\nFinal Target Speed: " + finalSpeed.ToString("0.0");
			AdjustThrottle(finalSpeed, useBrakes, useAB);



			if(steerMode == SteerModes.Aiming)
			{
				localAngVel -= localTargetAngVel;
			}

			Vector3 targetDirection;
			Vector3 targetDirectionYaw;
			float yawError;
			float pitchError;
			//float postYawFactor;
			//float postPitchFactor;
			if(steerMode == SteerModes.NormalFlight)
			{
				targetDirection = velocityTransform.InverseTransformDirection(targetPosition - velocityTransform.position).normalized;
				targetDirection = Vector3.RotateTowards(Vector3.up, targetDirection, 45 * Mathf.Deg2Rad, 0);

				targetDirectionYaw = vesselTransform.InverseTransformDirection(vessel.srf_velocity).normalized;
				targetDirectionYaw = Vector3.RotateTowards(Vector3.up, targetDirectionYaw, 45 * Mathf.Deg2Rad, 0);


				//postYawFactor = 0.25f;
				//postPitchFactor = 0.8f;
			}
			else//(steerMode == SteerModes.Aiming)
			{
				targetDirection = vesselTransform.InverseTransformDirection(targetPosition-vesselTransform.position).normalized;
				targetDirection = Vector3.RotateTowards(Vector3.up, targetDirection, 25 * Mathf.Deg2Rad, 0);
				targetDirectionYaw = targetDirection;

				/*
				if(command == PilotCommands.Follow)
				{
					postYawFactor = 0.45f;
					postPitchFactor = 1f;
				}
				else
				{
					postYawFactor = 1f;
					postPitchFactor = 1.2f;
				}
				*/
			}

			pitchError = VectorUtils.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(targetDirection, Vector3.right), Vector3.back);
			yawError = VectorUtils.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(targetDirectionYaw, Vector3.forward), Vector3.right);




			//test
			debugString += "\n   finalMaxSteer: " + finalMaxSteer;







			//roll
			Vector3 currentRoll = -vesselTransform.forward;
			float rollUp = (steerMode == SteerModes.Aiming ? 5f : 10f);
			if(steerMode == SteerModes.NormalFlight)
			{
				rollUp += (1 - finalMaxSteer) * 10f;
			}
			rollTarget = (targetPosition + (rollUp * upDirection)) - vesselTransform.position;

			//test
			if(steerMode == SteerModes.Aiming && !belowMinAltitude)
			{
				angVelRollTarget = -140 * vesselTransform.TransformVector(Quaternion.AngleAxis(90f, Vector3.up) * localTargetAngVel);
				rollTarget += angVelRollTarget;
			}

			if(command == PilotCommands.Follow && useRollHint)
			{
				rollTarget = -commandLeader.vessel.ReferenceTransform.forward;
			}

			//
			if(belowMinAltitude)
			{
				rollTarget = vessel.upAxis * 100;

			}
			if(useVelRollTarget && !belowMinAltitude)
			{
				rollTarget = Vector3.ProjectOnPlane(rollTarget, vessel.srf_velocity);
				currentRoll = Vector3.ProjectOnPlane(currentRoll, vessel.srf_velocity);
			}
			else
			{
				rollTarget = Vector3.ProjectOnPlane(rollTarget, vesselTransform.up);
			}

			//v/q
			float dynamicAdjustment = Mathf.Clamp(16*(float)(vessel.srfSpeed/vessel.dynamicPressurekPa), 0, 1.2f);

			float rollError = Misc.Misc.SignedAngle(currentRoll, rollTarget, vesselTransform.right);
			float steerRoll = (steerMult * 0.0015f * rollError);
			float rollDamping = (.10f * steerDamping * -localAngVel.y);
			steerRoll -= rollDamping;
			steerRoll *= dynamicAdjustment;

			if(steerMode == SteerModes.NormalFlight)
			{
				//premature dive fix
				pitchError = pitchError * Mathf.Clamp01((21 - Mathf.Exp(Mathf.Abs(rollError) / 30)) / 20);
			}

			float steerPitch = (0.015f * steerMult * pitchError) - (steerDamping * -localAngVel.x);
			float steerYaw = (0.005f * steerMult * yawError) - (steerDamping * 0.2f * -localAngVel.z);

			pitchIntegral += pitchError;

			steerPitch *= dynamicAdjustment;
			steerYaw *= dynamicAdjustment;

			float pitchKi = 0.1f * (pitchKiAdjust/5); //This is what should be allowed to be tweaked by the player, just like the steerMult, it is very low right now
			pitchIntegral = Mathf.Clamp(pitchIntegral, -0.2f / (pitchKi * dynamicAdjustment), 0.2f / (pitchKi * dynamicAdjustment)); //0.2f is the limit of the integral variable, making it bigger increases overshoot
			steerPitch += pitchIntegral * pitchKi * dynamicAdjustment; //Adds the integral component to the mix

			float roll = Mathf.Clamp(steerRoll, -maxSteer, maxSteer);
			s.roll = roll;
			s.yaw = Mathf.Clamp(steerYaw, -finalMaxSteer, finalMaxSteer);
			s.pitch = Mathf.Clamp(steerPitch, Mathf.Min(-finalMaxSteer, -0.2f), finalMaxSteer);
		}

		Vector3 rollTarget;
		Vector3 angVelRollTarget;


		void FlyExtend(FlightCtrlState s, Vector3 tPosition)
		{
			if(weaponManager)
			{
				if (weaponManager.TargetOverride)
				{
					extending = false;
					weaponManager.ForceWideViewScan();
				}
				else
					weaponManager.ForceWideViewScan();


				float extendDistance = Mathf.Clamp(weaponManager.guardRange-1800, 2500, 4000);

				if(weaponManager.CurrentMissile && weaponManager.CurrentMissile.GetWeaponClass() == WeaponClasses.Bomb)
				{
					extendDistance = 4500;
				}

				if(targetVessel!=null && !targetVessel.LandedOrSplashed)      //this is just asking for trouble at 800m
				{
					extendDistance = 1600;
				}

				Vector3 srfVector = Vector3.ProjectOnPlane(vessel.transform.position - tPosition, upDirection);
				float srfDist = srfVector.magnitude;
				if(srfDist < extendDistance)
				{
					Vector3 targetDirection = srfVector.normalized*extendDistance;
					Vector3 target = vessel.transform.position + targetDirection;
					target = GetTerrainSurfacePosition(target) + (vessel.upAxis*Mathf.Min(defaultAltitude, MissileGuidance.GetRaycastRadarAltitude(vesselTransform.position)));
					target = FlightPosition(target, defaultAltitude);
					if(regainEnergy)
					{
						RegainEnergy(s, target - vesselTransform.position);
						return;
					}
					else
					{
						FlyToPosition(s, target);
					}
				}
				else
				{
					extending = false;
				}
			}
			else
			{
				extending = false;
			}
		}

		void FlyOrbit(FlightCtrlState s, Vector3d centerGPS, float radius, float speed, bool clockwise)
		{
			if(regainEnergy)
			{
				RegainEnergy(s, vessel.srf_velocity);
				return;
			}

			finalMaxSteer = GetSteerLimiterForSpeedAndPower();

			debugString += "\nFlying orbit";
			Vector3 flightCenter = GetTerrainSurfacePosition(VectorUtils.GetWorldSurfacePostion(centerGPS, vessel.mainBody)) + (defaultAltitude*upDirection);


			Vector3 myVectorFromCenter = Vector3.ProjectOnPlane(vessel.transform.position - flightCenter, upDirection);
			Vector3 myVectorOnOrbit = myVectorFromCenter.normalized * radius;

			Vector3 targetVectorFromCenter = Quaternion.AngleAxis(clockwise ? 15f : -15f, upDirection) * myVectorOnOrbit;

			Vector3 verticalVelVector = Vector3.Project(vessel.srf_velocity, upDirection); //for vv damping

			Vector3 targetPosition = flightCenter + targetVectorFromCenter - (verticalVelVector * 0.25f);

			Vector3 vectorToTarget = targetPosition - vesselTransform.position;
			//Vector3 planarVel = Vector3.ProjectOnPlane(vessel.srf_velocity, upDirection);
			//vectorToTarget = Vector3.RotateTowards(planarVel, vectorToTarget, 25f * Mathf.Deg2Rad, 0);
			vectorToTarget = GetLimitedClimbDirectionForSpeed(vectorToTarget);
			targetPosition = vesselTransform.position + vectorToTarget;

			if(command != PilotCommands.Free && Vector3.Distance(vessel.transform.position, flightCenter) < radius*1.5f)
			{
				Debug.Log("[BDArmory]: AI Pilot reached command destination.");
				command = PilotCommands.Free;
			}

			useVelRollTarget = true;

			AdjustThrottle(speed, false);
			FlyToPosition(s, targetPosition);
		}

		//sends target speed to speedController
		void AdjustThrottle(float targetSpeed, bool useBrakes, bool allowAfterburner = true)
		{
			speedController.targetSpeed = targetSpeed;
			speedController.useBrakes = useBrakes;
			speedController.allowAfterburner = allowAfterburner;
		}

		Vector3 threatRelativePosition;
		void Evasive(FlightCtrlState s)
		{
			currentStatus = "Evading";
			debugString += "\nEvasive";
			debugString += "\n Threat Distance: " + weaponManager.incomingMissileDistance;

			collisionDetectionTicker += 2;


			if(weaponManager)
			{
				if(weaponManager.isFlaring)
				{
					useAB = vessel.srfSpeed < minSpeed;
					useBrakes = false;
					float targetSpeed = minSpeed;
					if(weaponManager.isChaffing)
					{
						targetSpeed = maxSpeed;
					}
					AdjustThrottle(targetSpeed, false, useAB);
				}


				if((weaponManager.isChaffing || weaponManager.isFlaring) && (weaponManager.incomingMissileDistance > 2000))
				{
					debugString += "\nBreaking from missile threat!";
					Vector3 axis = -Vector3.Cross(vesselTransform.up, threatRelativePosition);
					Vector3 breakDirection = Quaternion.AngleAxis(90, axis) * threatRelativePosition;
					//Vector3 breakTarget = vesselTransform.position + breakDirection;
					RegainEnergy(s, breakDirection);
					return;
				}
				else if(weaponManager.underFire)
				{
					debugString += "\nDodging gunfire";
					float threatDirectionFactor = Vector3.Dot(vesselTransform.up, threatRelativePosition.normalized);
					//Vector3 axis = -Vector3.Cross(vesselTransform.up, threatRelativePosition);

					Vector3 breakTarget = threatRelativePosition * 2f;       //for the most part, we want to turn _towards_ the threat in order to increase the rel ang vel and get under its guns

					if (threatDirectionFactor > 0.9f)     //within 28 degrees in front
					{
						breakTarget += Vector3.Cross(threatRelativePosition.normalized, Mathf.Sign(Mathf.Sin((float)vessel.missionTime / 2)) * vessel.upAxis);
						debugString += " from directly ahead!";
					}
					else if (threatDirectionFactor < -0.9) //within ~28 degrees behind
					{
						float threatDistance = threatRelativePosition.magnitude;
						if(threatDistance > 400)
						{
							breakTarget = vesselTransform.position + vesselTransform.up * 1500 - 500 * vessel.upAxis;
							breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vesselTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vesselTransform.forward * 1000;
							if(threatDistance > 800)
								debugString += " from behind afar; engaging barrel roll";
							else
							{
								debugString += " from behind moderate distance; engaging aggressvie barrel roll and braking";
								steerMode = SteerModes.Aiming;
								AdjustThrottle(minSpeed, true, false);
							}
						}
						else
						{
							breakTarget = threatRelativePosition;
							if (evasiveTimer < 1.5f)
								breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vesselTransform.right * 500;
							else
								breakTarget += -Math.Sign(Mathf.Sin((float)vessel.missionTime * 2)) * vesselTransform.right * 150;
							debugString += " from directly behind and close; breaking hard";
							steerMode = SteerModes.Aiming;
						}
					}
					else
					{
						float threatDistance = threatRelativePosition.magnitude;
						if(threatDistance < 400)
						{
							breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vesselTransform.right * 100;
							debugString += " from the side; breaking in";
							steerMode = SteerModes.Aiming;
						}
						else
						{
							breakTarget = vesselTransform.position + vesselTransform.up * 1500;
							breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vesselTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vesselTransform.forward * 1000;
							debugString += " from far side; engaging barrel roll";
						}
					}

					float threatAltitudeDiff = Vector3.Dot(threatRelativePosition, vessel.upAxis);
					if (threatAltitudeDiff > 500)
						breakTarget += threatAltitudeDiff * vessel.upAxis;      //if it's trying to spike us from below, don't go crazy trying to dive below it
					else
						breakTarget += - 150 * vessel.upAxis;   //dive a bit to escape

					FlyToPosition(s, breakTarget);
					return;

				}
				else if(weaponManager.incomingMissileVessel)
				{
					float mSqrDist = Vector3.SqrMagnitude(weaponManager.incomingMissileVessel.transform.position - vesselTransform.position);
					if(mSqrDist < 810000) //900m
					{
						debugString += "\nMissile about to impact! pull away!";
						AdjustThrottle(maxSpeed, false, false);
						Vector3 cross = Vector3.Cross(weaponManager.incomingMissileVessel.transform.position - vesselTransform.position, vessel.srf_velocity).normalized;
						if(Vector3.Dot(cross, -vesselTransform.forward) < 0)
						{
							cross = -cross;
						}
						FlyToPosition(s, vesselTransform.position +(50*vessel.srf_velocity/vessel.srfSpeed)+ (100 * cross));
						return;
					}



				}
			}

			Vector3 target = (vessel.srfSpeed < 200) ? FlightPosition(vessel.transform.position, minAltitude) : vesselTransform.position;
			float angleOff = Mathf.Sin(Time.time * 0.75f) * 180;
			angleOff = Mathf.Clamp(angleOff, -45, 45);
			target +=
				(Quaternion.AngleAxis(angleOff, upDirection) * Vector3.ProjectOnPlane(vesselTransform.up * 500, upDirection));
			//+ (Mathf.Sin (Time.time/3) * upDirection * minAltitude/3);


			FlyToPosition(s, target);
		}

		void TakeOff(FlightCtrlState s)
		{
			debugString += "\nTaking off/Gaining altitude";

			if(vessel.LandedOrSplashed && vessel.srfSpeed < takeOffSpeed)
			{
				defaultOrbitCoords = VectorUtils.WorldPositionToGeoCoords(vessel.transform.position, vessel.mainBody);
				return;
			}

			steerMode = SteerModes.Aiming;

			float radarAlt = MissileGuidance.GetRadarAltitude(vessel);

			Vector3 forwardPoint = vessel.transform.position + Vector3.ProjectOnPlane((vessel.horizontalSrfSpeed < 10 ? vesselTransform.up : (Vector3)vessel.srf_vel_direction) * 100, upDirection);
			float terrainDiff = MissileGuidance.GetRaycastRadarAltitude(forwardPoint) - radarAlt;
			terrainDiff = Mathf.Max(terrainDiff, 0);

			float rise = Mathf.Clamp((float)vessel.srfSpeed * 0.3f, 5, 100);

			if(radarAlt > 70)
			{
				vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false);
			}
			else
			{
				vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
			}

			FlyToPosition(s, forwardPoint + (upDirection * (rise+terrainDiff)));

			if(radarAlt > minAltitude)
			{
				belowMinAltitude = false;
			}
		}

		Vector3 GetLimitedClimbDirectionForSpeed(Vector3 direction)
		{
			if(Vector3.Dot(direction, upDirection) < 0) 
			{
				debugString += "\nclimb limit angle: unlimited";
				return direction; //only use this if climbing
			}

			Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDirection).normalized * 100;

			float angle = Mathf.Clamp((float)vessel.srfSpeed * 0.13f, 5, 90);

			debugString += "\nclimb limit angle: " + angle.ToString("0.0");
			return Vector3.RotateTowards(planarDirection, direction, angle*Mathf.Deg2Rad, 0);
		}

		void UpdateGAndAoALimits(FlightCtrlState s)
		{
			if (vessel.dynamicPressurekPa <= 0 || vessel.srfSpeed < takeOffSpeed || belowMinAltitude && -Vector3.Dot(vessel.ReferenceTransform.forward, vessel.upAxis) < 0.8f)
			{
				return;
			}

			if(lastAllowedAoA != maxAllowedAoA)
			{
				lastAllowedAoA = maxAllowedAoA;
				maxAllowedCosAoA = (float)Math.Cos(lastAllowedAoA * Math.PI / 180.0);
			}
			float pitchG = -Vector3.Dot(vessel.acceleration, vessel.ReferenceTransform.forward);       //should provide g force in vessel up / down direction, assuming a standard plane
			float pitchGPerDynPres = pitchG / (float)vessel.dynamicPressurekPa;

			float curCosAoA = Vector3.Dot(vessel.srf_velocity.normalized, vessel.ReferenceTransform.forward);

			//adjust moving averages
			//adjust gLoad average
			gLoadMovingAvg *= 32f;
			gLoadMovingAvg -= gLoadMovingAvgArray[movingAvgIndex];
			gLoadMovingAvgArray[movingAvgIndex] = pitchGPerDynPres;
			gLoadMovingAvg += pitchGPerDynPres;
			gLoadMovingAvg /= 32f;

			//adjusting cosAoAAvg
			cosAoAMovingAvg *= 32f;
			cosAoAMovingAvg -= cosAoAMovingAvgArray[movingAvgIndex];
			cosAoAMovingAvgArray[movingAvgIndex] = curCosAoA;
			cosAoAMovingAvg += curCosAoA;
			cosAoAMovingAvg /= 32f;

			++movingAvgIndex;
			if (movingAvgIndex == gLoadMovingAvgArray.Length)
				movingAvgIndex = 0;

			if (gLoadMovingAvg < maxNegG || Math.Abs(cosAoAMovingAvg - cosAoAAtMaxNegG) < 0.005f)
			{
				maxNegG = gLoadMovingAvg;
				cosAoAAtMaxNegG = cosAoAMovingAvg;
			}
			if (gLoadMovingAvg > maxPosG || Math.Abs(cosAoAMovingAvg - cosAoAAtMaxPosG) < 0.005f)
			{
				maxPosG = gLoadMovingAvg;
				cosAoAAtMaxPosG = cosAoAMovingAvg;
			}

			if(cosAoAAtMaxNegG >= cosAoAAtMaxPosG)
			{
				cosAoAAtMaxNegG = cosAoAAtMaxPosG = maxNegG = maxPosG = 0;
				gOffsetPerDynPres = gaoASlopePerDynPres = 0;
				return;
			}

			if (maxPosG > maxDynPresGRecorded)
				maxDynPresGRecorded = maxPosG;

			float aoADiff = cosAoAAtMaxPosG - cosAoAAtMaxNegG;

			//if (Math.Abs(pitchControlDiff) < 0.005f)
			//    return;                 //if the pitch control values are too similar, don't bother to avoid numerical errors


			gaoASlopePerDynPres = (maxPosG - maxNegG) / aoADiff;
			gOffsetPerDynPres = maxPosG - gaoASlopePerDynPres * cosAoAAtMaxPosG;     //g force offset
		}

		void AdjustPitchForGAndAoALimits(FlightCtrlState s)
		{
			float minCosAoA, maxCosAoA;
			//debugString += "\nMax Pos G: " + maxPosG + " @ " + cosAoAAtMaxPosG;
			//debugString += "\nMax Neg G: " + maxNegG + " @ " + cosAoAAtMaxNegG;

			if (vessel.LandedOrSplashed || vessel.srfSpeed < Math.Min(minSpeed, takeOffSpeed))         //if we're going too slow, don't use this
			{
				float speed = Math.Max(takeOffSpeed, minSpeed);
				negPitchDynPresLimitIntegrator = -1f * 0.001f * 0.5f * 1.225f * speed * speed;
				posPitchDynPresLimitIntegrator = 1f * 0.001f * 0.5f * 1.225f * speed * speed;
				return;
			}

			float invVesselDynPreskPa = 1f / (float)vessel.dynamicPressurekPa;

			maxCosAoA = maxAllowedGForce * 9.81f * invVesselDynPreskPa;
			minCosAoA = -maxCosAoA;

			maxCosAoA -= gOffsetPerDynPres;
			minCosAoA -= gOffsetPerDynPres;

			maxCosAoA /= gaoASlopePerDynPres;
			minCosAoA /= gaoASlopePerDynPres;

			if (maxCosAoA > maxAllowedCosAoA)
				maxCosAoA = maxAllowedCosAoA;

			if (minCosAoA < -maxAllowedCosAoA)
				minCosAoA = -maxAllowedCosAoA;

			float curCosAoA = Vector3.Dot(vessel.srf_velocity / vessel.srfSpeed, vessel.ReferenceTransform.forward);


			float centerCosAoA = (minCosAoA + maxCosAoA) * 0.5f;
			float curCosAoACentered = curCosAoA - centerCosAoA;
			float cosAoADiff = 0.5f * Math.Abs(maxCosAoA - minCosAoA);
			float curCosAoANorm = curCosAoACentered / cosAoADiff;      //scaled so that from centerAoA to maxAoA is 1


			float negPitchScalar, posPitchScalar;
			negPitchScalar = negPitchDynPresLimitIntegrator * invVesselDynPreskPa - lastPitchInput;
			posPitchScalar = lastPitchInput - posPitchDynPresLimitIntegrator * invVesselDynPreskPa;

			//update pitch control limits as needed
			float negPitchDynPresLimit, posPitchDynPresLimit;
			negPitchDynPresLimit = posPitchDynPresLimit = 0;
			if (curCosAoANorm < -0.15f)// || Math.Abs(negPitchScalar) < 0.01f)
			{
				float cosAoAOffset = curCosAoANorm + 1;     //set max neg aoa to be 0
				float aoALimScalar = Math.Abs(curCosAoANorm);
				aoALimScalar *= aoALimScalar;
				aoALimScalar *= aoALimScalar;
				aoALimScalar *= aoALimScalar;
				if (aoALimScalar > 1)
					aoALimScalar = 1;

				float pitchInputScalar = negPitchScalar;
				pitchInputScalar = 1 - Mathf.Clamp01(Math.Abs(pitchInputScalar));
				pitchInputScalar *= pitchInputScalar;
				pitchInputScalar *= pitchInputScalar;
				pitchInputScalar *= pitchInputScalar;
				if (pitchInputScalar < 0)
					pitchInputScalar = 0;

				float deltaCosAoANorm = curCosAoA - lastCosAoA;
				deltaCosAoANorm /= cosAoADiff;

				debugString += "\nUpdating Neg Gs";
				negPitchDynPresLimitIntegrator -= 0.01f * Mathf.Clamp01(aoALimScalar + pitchInputScalar) * cosAoAOffset * (float)vessel.dynamicPressurekPa;
				negPitchDynPresLimitIntegrator -= 0.005f * deltaCosAoANorm * (float)vessel.dynamicPressurekPa;
				if (cosAoAOffset < 0)
					negPitchDynPresLimit = -0.3f * cosAoAOffset;
			}
			if (curCosAoANorm > 0.15f)// || Math.Abs(posPitchScalar) < 0.01f)
			{
				float cosAoAOffset = curCosAoANorm - 1;     //set max pos aoa to be 0
				float aoALimScalar = Math.Abs(curCosAoANorm);
				aoALimScalar *= aoALimScalar;
				aoALimScalar *= aoALimScalar;
				aoALimScalar *= aoALimScalar;
				if (aoALimScalar > 1)
					aoALimScalar = 1;

				float pitchInputScalar = posPitchScalar;
				pitchInputScalar = 1 - Mathf.Clamp01(Math.Abs(pitchInputScalar));
				pitchInputScalar *= pitchInputScalar;
				pitchInputScalar *= pitchInputScalar;
				pitchInputScalar *= pitchInputScalar;
				if (pitchInputScalar < 0)
					pitchInputScalar = 0;

				float deltaCosAoANorm = curCosAoA - lastCosAoA;
				deltaCosAoANorm /= cosAoADiff;

				debugString += "\nUpdating Pos Gs";
				posPitchDynPresLimitIntegrator -= 0.01f * Mathf.Clamp01(aoALimScalar + pitchInputScalar) * cosAoAOffset * (float)vessel.dynamicPressurekPa;
				posPitchDynPresLimitIntegrator -= 0.005f * deltaCosAoANorm * (float)vessel.dynamicPressurekPa;
				if(cosAoAOffset > 0)
					posPitchDynPresLimit = -0.3f * cosAoAOffset;
			}

			float currentG = -Vector3.Dot(vessel.acceleration, vessel.ReferenceTransform.forward);
			float negLim, posLim;
			negLim = negPitchDynPresLimitIntegrator * invVesselDynPreskPa + negPitchDynPresLimit;
			if (negLim > s.pitch)
			{
				if (currentG > -(maxAllowedGForce * 0.97f * 9.81f))
				{
					negPitchDynPresLimitIntegrator -= (float)(0.15 * vessel.dynamicPressurekPa);        //jsut an override in case things break

					maxNegG = currentG * invVesselDynPreskPa;
					cosAoAAtMaxNegG = curCosAoA;

					negPitchDynPresLimit = 0;

					//maxPosG = 0;
					//cosAoAAtMaxPosG = 0;
				}

				s.pitch = negLim;
				debugString += "\nLimiting Neg Gs";
			}
			posLim = posPitchDynPresLimitIntegrator * invVesselDynPreskPa + posPitchDynPresLimit;
			if (posLim < s.pitch)
			{
				if (currentG < (maxAllowedGForce * 0.97f * 9.81f))
				{
					posPitchDynPresLimitIntegrator += (float)(0.15 * vessel.dynamicPressurekPa);        //jsut an override in case things break

					maxPosG = currentG * invVesselDynPreskPa;
					cosAoAAtMaxPosG = curCosAoA;

					posPitchDynPresLimit = 0;

					//maxNegG = 0;
					//cosAoAAtMaxNegG = 0;
				}

				s.pitch = posLim;
				debugString += "\nLimiting Pos Gs";
			}            

			lastPitchInput = s.pitch;
			lastCosAoA = curCosAoA;
			debugString += "\nNeg Pitch Lim: " + negLim;
			debugString += "\nPos Pitch Lim: " + posLim;

		}

		void CalculateAccelerationAndTurningCircle()
		{
			maxLiftAcceleration = maxDynPresGRecorded;
			maxLiftAcceleration *= (float)vessel.dynamicPressurekPa;       //maximum acceleration from lift that the vehicle can provide

			maxLiftAcceleration = Math.Min(maxLiftAcceleration, maxAllowedGForce * 9.81f);       //limit it to whichever is smaller, what we can provide or what we can handle
			maxLiftAcceleration = maxAllowedGForce * 9.81f;

			if(maxLiftAcceleration > 0)
				turnRadius = (float)vessel.srf_velocity.sqrMagnitude / maxLiftAcceleration;     //radius that we can turn in assuming constant velocity, assuming simple circular motion
		}

		float MinAltitudeNeeded()         //min altitude adjusted for G limits; let's try _not_ to overcook dives and faceplant into the ground
		{
			//for a pure vertical dive, turnRadius will be the altitude that we need to turn.  However, for shallower dives we don't need that much.  Let's account for that.
			//actual altitude needed will be radius * (1 - cos(theta)), where theta is the angle of the arc from dive entry to the turning circle to the bottom
			//we can calculate that from the velocity vector mag dotted with the up vector

			float diveAngleCorrection = -Vector3.Dot(vessel.srf_velocity / vessel.srfSpeed, vessel.upAxis); //normalize the vector and dot it with upAxis
			//this gives us sin(theta)
			if(diveAngleCorrection > 0)         //we're headed downwards
			{
				diveAngleCorrection *= diveAngleCorrection;
				diveAngleCorrection = 1 - diveAngleCorrection;
				diveAngleCorrection = Math.Max(0f, diveAngleCorrection);    //remember to check to make sure numerical errors haven't crept in!  Can't have NaN showing up
				diveAngleCorrection = Mathf.Sqrt(diveAngleCorrection);      //convert sin(theta) to cos(theta)

				diveAngleCorrection = 1 - diveAngleCorrection;      //and convert to 1 - cos(theta)
			}
			else
			{
				diveAngleCorrection = 0;
			}

			return Math.Max(minAltitude, 100 + turnRadius * diveAngleCorrection);
		}

		Vector3 DefaultAltPosition()
		{
			return (vessel.transform.position + (-(float)vessel.altitude*upDirection) + (defaultAltitude *upDirection));
		}

		Vector3 GetSurfacePosition(Vector3 position)
		{
			return position - ((float)FlightGlobals.getAltitudeAtPos(position) * upDirection);
		}

		Vector3 GetTerrainSurfacePosition(Vector3 position)
		{
			return position - (MissileGuidance.GetRaycastRadarAltitude(position) * upDirection);
		}


		Vector3 FlightPosition(Vector3 targetPosition, float minAlt)
		{
			Vector3 forwardDirection = vesselTransform.up;
			Vector3 targetDirection = (targetPosition - vesselTransform.position).normalized;

			float vertFactor = 0;
			vertFactor += (((float)vessel.srfSpeed / minSpeed) - 2f) * 0.3f;          //speeds greater than 2x minSpeed encourage going upwards; below encourages downwards
			vertFactor += (((targetPosition - vesselTransform.position).magnitude / 1000f) - 1f) * 0.3f;    //distances greater than 1000m encourage going upwards; closer encourages going downwards
			vertFactor -= Mathf.Clamp01(Vector3.Dot(vesselTransform.position - targetPosition, upDirection) / 1600f - 1f) * 0.5f;       //being higher than 1600m above a target encourages going downwards
			if (targetVessel)
				vertFactor += Vector3.Dot(targetVessel.srf_velocity / targetVessel.srfSpeed, (targetVessel.ReferenceTransform.position - vesselTransform.position).normalized) * 0.3f;   //the target moving away from us encourages upward motion, moving towards us encourages downward motion
			else
				vertFactor += 0.4f;
			vertFactor -= weaponManager.underFire ? 0.5f : 0;   //being under fire encourages going downwards as well, to gain energy

			float alt = MissileGuidance.GetRadarAltitude(vessel);

			if (vertFactor > 2)
				vertFactor = 2;
			if (vertFactor < -2)
				vertFactor = -2;

			/*if ((weaponManager.TargetOverride && vessel.srfSpeed > minSpeed * 3) || 
                (!weaponManager.TargetOverride && (vessel.srfSpeed > minSpeed * 1.5f)) && 
                Vector3.Dot(targetPosition-vesselTransform.position, upDirection) > -1000 && (
                ((targetPosition - vesselTransform.position).magnitude > 1200 || targetVessel == null || vessel.srfSpeed > targetVessel.srfSpeed * 1.5f) || 
                (Vector3.Dot(forwardDirection, upDirection) > 0.4f && Vector3.Dot(vesselTransform.forward, targetDirection) < 0)) &&
                !weaponManager.underFire)      //go upwards in some way
            {
                vertFactor = (float)vessel.srfSpeed / (minSpeed * 1.5f);
                --vertFactor;
                vertFactor *= 0.8f;
                vertFactor = Mathf.Clamp01(vertFactor);
            }
            else if(alt > minAltitude)
            {
                vertFactor = (alt - minAltitude) / (2 * turnRadius);
                vertFactor = Mathf.Clamp01(vertFactor);
                vertFactor = -vertFactor;
            }*/

			vertFactor += 0.15f * Mathf.Sin((float)vessel.missionTime * 0.25f);     //some randomness in there

			Vector3 projectedDirection = Vector3.ProjectOnPlane(forwardDirection, upDirection);
			Vector3 projectedTargetDirection = Vector3.ProjectOnPlane(targetDirection, upDirection);
			if(Vector3.Dot(targetDirection, forwardDirection) < 0)
			{
				if(Vector3.Angle(projectedTargetDirection, projectedDirection) > 165f)
				{
					targetPosition = vesselTransform.position + (Quaternion.AngleAxis(Mathf.Sign(Mathf.Sin((float)vessel.missionTime / 4)) * 45, upDirection) * (projectedDirection.normalized * 200));
					targetDirection = (targetPosition - vesselTransform.position).normalized;
				}

				targetPosition = vesselTransform.position + Vector3.RotateTowards(projectedDirection, Vector3.ProjectOnPlane(targetDirection, upDirection) + upDirection * vertFactor, 45*Mathf.Deg2Rad, 0).normalized*200;
			}
			else if(steerMode != SteerModes.Aiming)
			{
				float distance = (targetPosition - vesselTransform.position).magnitude;
				if (vertFactor < 0)
					distance = Math.Min(distance, Math.Abs((alt - minAlt) / vertFactor));

				targetPosition += upDirection * Math.Min(distance, 1000) * vertFactor * (1 - Math.Abs(Vector3.Dot(projectedTargetDirection, projectedDirection)));
				/*if (vertFactor < 0)
                    targetPosition += upDirection * Math.Min((alt - minAltitude), (targetPosition - vesselTransform.position).magnitude) * 0.2f * vertFactor * (1 - Math.Abs(Vector3.Dot(projectedTargetDirection, projectedDirection)));
                else
                {
                    if(targetVessel)
                        vertFactor *= (1f + Mathf.Clamp((float)(vessel.srfSpeed - targetVessel.srfSpeed), 0, float.PositiveInfinity) * 0.05f);
                    if (Vector3.Dot(targetPosition - vesselTransform.position, upDirection) > 0)
                        targetPosition += upDirection * Vector3.Dot(targetPosition - vesselTransform.position, upDirection) * vertFactor * (1 - Math.Abs(Vector3.Dot(projectedTargetDirection, projectedDirection)));
                    else
                        targetPosition += upDirection * (targetPosition - vesselTransform.position).magnitude * vertFactor * (1 - Math.Abs(Vector3.Dot(projectedTargetDirection, projectedDirection)));
                }*/
			}


			if(MissileGuidance.GetRadarAltitude(vessel) > minAlt * 1.1f)
			{
				return targetPosition;
			}

			float pointRadarAlt = MissileGuidance.GetRaycastRadarAltitude(targetPosition);
			if(pointRadarAlt < minAlt)
			{
				float adjustment = (minAlt-pointRadarAlt);
				debugString += "\nTarget position is below minAlt. Adjusting by "+adjustment;
				return targetPosition + (adjustment * upDirection);
			}
			else
			{
				return targetPosition;
			}
		}

		public bool GetLaunchAuthorization(Vessel targetV, MissileFire mf)
		{
			bool launchAuthorized = false;
			Vector3 target = targetV.transform.position;
			MissileBase missile = mf.CurrentMissile;
			if(missile != null)
			{
				if(!targetV.LandedOrSplashed)
				{
					target = MissileGuidance.GetAirToAirFireSolution(missile, targetV);
				}

				float boresightFactor = targetV.LandedOrSplashed ? 0.75f : 0.35f;

				//if(missile.TargetingMode == MissileBase.TargetingModes.Gps) maxOffBoresight = 45;

				float fTime = 2f;
				Vector3 futurePos = target + (targetV.srf_velocity * fTime);
				Vector3 myFuturePos = vesselTransform.position + (vessel.srf_velocity * fTime);
				bool fDot = Vector3.Dot(vesselTransform.up, futurePos - myFuturePos) > 0; //check target won't likely be behind me soon

               if (fDot && Vector3.Angle(missile.GetForwardTransform(), target - missile.transform.position) < missile.maxOffBoresight * boresightFactor)
               {
                        launchAuthorized = true;
               }
                    
			}

			return launchAuthorized;
		}

		void GetGuardTarget()
		{
			if(weaponManager!=null && weaponManager.vessel == vessel)
			{
				if(weaponManager.guardMode && weaponManager.currentTarget!=null)
				{
					targetVessel = weaponManager.currentTarget.Vessel;
				}
				else
				{
					targetVessel = null;
				}
				weaponManager.pilotAI = this;
				return;
			}
			else
			{
			    List<MissileFire>.Enumerator mfs = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
                while (mfs.MoveNext())
                {
                    if (mfs.Current == null) continue;
					targetVessel = mfs.Current.currentTarget!=null 
                        ? mfs.Current.currentTarget.Vessel 
                        : null;

					weaponManager = mfs.Current;
                    mfs.Current.pilotAI = this;

					return;
				}
                mfs.Dispose();
			}
		}

		bool DetectCollision(Vector3 direction, out Vector3 badDirection)
		{
			badDirection = Vector3.zero;
			if(MissileGuidance.GetRadarAltitude(vessel) < 20) return false;

			direction = direction.normalized;
			int layerMask = 1<<15;
			Ray ray = new Ray(vesselTransform.position + (50*vesselTransform.up), direction);
			float distance = Mathf.Clamp((float)vessel.srfSpeed * 4f, 125f, 2500);
			RaycastHit hit;
		    if (!Physics.SphereCast(ray, 10, out hit, distance, layerMask)) return false;
		    Rigidbody otherRb = hit.collider.attachedRigidbody;
		    if(otherRb)
		    {
		        if (!(Vector3.Dot(otherRb.velocity, vessel.srf_velocity) < 0)) return false;
		        badDirection = hit.point - ray.origin;
		        return true;
		    }
		    badDirection = hit.point - ray.origin;
		    return true;
		}

		void UpdateCommand(FlightCtrlState s)
		{
			if(command == PilotCommands.Follow && !commandLeader)
			{
				ReleaseCommand();
				return;
			}

			if(command == PilotCommands.Follow)
			{
				currentStatus = "Follow";
				UpdateFollowCommand(s);
			}
			else if(command == PilotCommands.FlyTo)
			{
				currentStatus = "Fly To";
				FlyOrbit(s, defaultOrbitCoords, 2500, idleSpeed, true);
			}
			else if(command == PilotCommands.Attack)
			{
				currentStatus = "Attack";
				FlyOrbit(s, defaultOrbitCoords, 4500, maxSpeed, true);
			}
		}

		void UpdateFollowCommand(FlightCtrlState s)
		{
			steerMode = SteerModes.NormalFlight;
			vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);

			commandSpeed = commandLeader.vessel.srfSpeed;
			commandHeading = commandLeader.vessel.srf_velocity.normalized;

			//formation position
			commandPosition = GetFormationPosition();

			float distanceToPos = Vector3.Distance(vesselTransform.position, commandPosition);



			float dotToPos = Vector3.Dot(vesselTransform.up, commandPosition - vesselTransform.position);
			Vector3 flyPos;
			useRollHint = false;

			float ctrlModeThresh = 1000;

			if(distanceToPos < ctrlModeThresh)
			{
				flyPos = commandPosition + (ctrlModeThresh * commandHeading);

				Vector3 vectorToFlyPos = flyPos - vessel.ReferenceTransform.position;
				Vector3 projectedPosOffset = Vector3.ProjectOnPlane(commandPosition - vessel.ReferenceTransform.position, commandHeading);
				float posOffsetMag = projectedPosOffset.magnitude;
				float adjustAngle = (Mathf.Clamp(posOffsetMag * 0.27f, 0, 25));
				Vector3 projVel = Vector3.Project(vessel.srf_velocity - commandLeader.vessel.srf_velocity, projectedPosOffset);
				adjustAngle -= Mathf.Clamp(Mathf.Sign(Vector3.Dot(projVel, projectedPosOffset)) * projVel.magnitude * 0.12f, -10, 10);

				adjustAngle *= Mathf.Deg2Rad;

				vectorToFlyPos = Vector3.RotateTowards(vectorToFlyPos, projectedPosOffset, adjustAngle, 0);

				flyPos = vessel.ReferenceTransform.position + vectorToFlyPos;

				if(distanceToPos < 400)
				{
					steerMode = SteerModes.Aiming;
				}
				else
				{
					steerMode = SteerModes.NormalFlight;
				}

				if(distanceToPos < 10)
				{
					useRollHint = true;
				}
			}
			else
			{
				steerMode = SteerModes.NormalFlight;
				flyPos = commandPosition;
			}

			double finalMaxSpeed = commandSpeed;
			if(dotToPos > 0)
			{
				finalMaxSpeed += (distanceToPos / 8);
			}
			else
			{
				finalMaxSpeed -= (distanceToPos / 2);
			}


			AdjustThrottle((float)finalMaxSpeed, true);


			FlyToPosition(s, flyPos);
		}

		Vector3d GetFormationPosition()
		{
			Quaternion origVRot = velocityTransform.rotation;
			Vector3 origVLPos = velocityTransform.localPosition;

			velocityTransform.position = commandLeader.vessel.ReferenceTransform.position;
			if(commandLeader.vessel.srf_velocity != Vector3d.zero)
			{
				velocityTransform.rotation = Quaternion.LookRotation(commandLeader.vessel.srf_velocity, upDirection);
				velocityTransform.rotation = Quaternion.AngleAxis(90, velocityTransform.right) * velocityTransform.rotation;
			}
			else
			{
				velocityTransform.rotation = commandLeader.vessel.ReferenceTransform.rotation;
			}

			/*
			Vector3 lateralVelVector = Vector3.Project(vessel.srf_velocity, velocityTransform.right)/2;
			Vector3.ClampMagnitude(lateralVelVector, 25);

			Vector3 verticalVelVector = Vector3.Project(vessel.srf_velocity - commandLeader.vessel.srf_velocity, velocityTransform.forward) * 1;//0.75f; //MOVE THIS TO UpdateFollowCommand TO REGULATE VERTICAL POSITION AND SPEED
			Vector3.ClampMagnitude(verticalVelVector, 25);
			*/

			Vector3d pos = velocityTransform.TransformPoint(GetLocalFormationPosition(commandFollowIndex));// - lateralVelVector - verticalVelVector;

			velocityTransform.localPosition = origVLPos;
			velocityTransform.rotation = origVRot;

			return pos;
		}

		Vector3d GetLocalFormationPosition(int index)
		{
			float indexF = (float)index;
			indexF++;

			double rightSign = indexF % 2 == 0 ? -1 : 1;
			double positionFactor = Math.Ceiling(indexF/ 2);
			double spread = commandLeader.spread;
			double lag = commandLeader.lag;

			double right = rightSign * positionFactor * spread;
			double back = positionFactor * lag * -1;

			return new Vector3d(right, back, 0);
		}


		public void ReleaseCommand()
		{
		    if (!vessel || command == PilotCommands.Free) return;
		    if(command == PilotCommands.Follow)
		    {
		        if(commandLeader)
		        {
		            List<BDModulePilotAI>.Enumerator pilots = commandLeader.vessel.FindPartModulesImplementing<BDModulePilotAI>().GetEnumerator();
                    while (pilots.MoveNext())
                    {
                        if (pilots.Current == null) continue;
                        pilots.Current.isLeadingFormation = false;
		            }
                    pilots.Dispose();
		        }
		    }
		    Debug.Log(vessel.vesselName + " was released from command.");
		    command = PilotCommands.Free;

		    defaultOrbitCoords = VectorUtils.WorldPositionToGeoCoords(vesselTransform.position, vessel.mainBody);
		}

		public void CommandFollow(ModuleWingCommander leader, int followerIndex)
		{
		    if (!pilotEnabled) return;
		    if (leader == vessel || followerIndex < 0) return;

		    Debug.Log(vessel.vesselName + " was commanded to follow.");
		    command = PilotCommands.Follow;
		    commandLeader = leader;
		    commandFollowIndex = followerIndex;
		    List<BDModulePilotAI>.Enumerator pilots = commandLeader.vessel.FindPartModulesImplementing<BDModulePilotAI>().GetEnumerator();
		    while (pilots.MoveNext())
		    {
		        if (pilots.Current == null) continue;
		        pilots.Current.isLeadingFormation = true;
		    }
            pilots.Dispose();
		}

		public void CommandAG(KSPActionGroup ag)
		{
			if(!pilotEnabled) return;
			vessel.ActionGroups.ToggleGroup(ag);
		}

		public void CommandFlyTo(Vector3 gpsCoords)
		{
			if(!pilotEnabled) return;

			Debug.Log(vessel.vesselName + " was commanded to fly to.");
			defaultOrbitCoords = gpsCoords;
			commandGeoPos = gpsCoords;
			command = PilotCommands.FlyTo;
		}

		public void CommandAttack(Vector3 gpsCoords)
		{
		    if (!pilotEnabled) return;

			Debug.Log(vessel.vesselName + " was commanded to attack.");
			defaultOrbitCoords = gpsCoords;
			commandGeoPos = gpsCoords;
			command = PilotCommands.Attack;
		}

		void OnGUI()
		{
		    if (!pilotEnabled || !vessel.isActiveVessel) return;
		    if(BDArmorySettings.DRAW_DEBUG_LABELS)
		    {
		        GUI.Label(new Rect(200, Screen.height - 200, 400, 400), this.vessel.name+":"+ debugString);	
		    }

		    if (!BDArmorySettings.DRAW_DEBUG_LINES) return;
		    if(command == PilotCommands.Follow)
		    {
		        BDGUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, commandPosition, 2, Color.red);
		    }

		    BDGUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + rollTarget, 2, Color.blue);
		    BDGUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position + (0.05f * vesselTransform.right), vesselTransform.position + (0.05f * vesselTransform.right) + angVelRollTarget, 2, Color.green);
		}
	}
}
