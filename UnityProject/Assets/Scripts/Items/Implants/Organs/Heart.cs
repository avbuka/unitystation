﻿using System;
using System.Collections;
using System.Collections.Generic;
using HealthV2;
using UnityEngine;

public class Heart : BodyPart
{
	//The actual heartrate of this implant, in BPM.
	/// <summary>
	/// Heart Rate in BPM (beats per minute).
	/// </summary>
	private float heartRate = 0;

	[SerializeField] [Tooltip("How quickly the heart can change its heartrate per second. Human maximum is around 10.")]
	private float heartRateDelta = 10;

	[SerializeField] [Tooltip("The maximum heartrate of the implant. Human maximum is about 200.")]
	private float maxHeartRate = 200;

	[SerializeField]
	[Tooltip("Arbitrary rating of 'strengh'. Used to determine how much bloodflow the heart can provide." +
	         "100 is a human heart." +
	         "The higher this is, the more total blood can be pushed around per heartbeat.")]
	private float heartStrength = 100f;

	private float lastHeartBeat = 0f;
	private float nextHeartBeat = 0f;

	private float heartBeatDelay;

	public bool HeartAttack = false;

	System.Random RNGesus = new System.Random();


	public bool CanTriggerHeartAttack = true;

	public int SecondsOfRevivePulse = 30;

	public int CurrentPulse = 0;

	public override void ImplantPeriodicUpdate(LivingHealthMasterBase healthMaster)
	{
		base.ImplantPeriodicUpdate(healthMaster);
		if (healthMaster.OverallHealth < -100)
		{

			if (CanTriggerHeartAttack)
			{
				DoHeartAttack();
				CanTriggerHeartAttack = false;
				CurrentPulse = 0;
				return;
			}

			if (HeartAttack == false)
			{
				CurrentPulse++;
				if (SecondsOfRevivePulse < CurrentPulse)
				{
					DoHeartAttack();
				}
			}



		}
		else if (healthMaster.OverallHealth < -50)
		{
			CanTriggerHeartAttack = true;
			CurrentPulse = 0;
		}


		DoHeartBeat(healthMaster);
	}

	public void DoHeartBeat(LivingHealthMasterBase healthMaster)
	{
		//If we actually have a circulatory system.
		if (HeartAttack)
		{
			if (SecondsOfRevivePulse < CurrentPulse) return;
			var RNGInt = RNGesus.Next(0, 10000);
			if (RNGInt > 9990)
			{
				HeartAttack = false;
			}

			return;
		}

		CirculatorySystemBase circulatorySystem = healthMaster.CirculatorySystem;
		if (circulatorySystem)
		{
			circulatorySystem.HeartBeat(heartStrength * TotalModified);

			UpdateHeartRate(circulatorySystem);
		}
	}


	private void UpdateHeartRate(CirculatorySystemBase circulatorySystem)
	{
		//This may seem strange, but the heart itself isn't in charge of when to increase its heart rate.
		//The circulatory system will let us know if that needs changing.
		if (circulatorySystem.HeartRateNeedsIncreasing)
		{
			heartRate += Math.Min((Time.time - lastHeartBeat) * heartRateDelta, heartRateDelta);
			heartRate = Math.Min(heartRate, maxHeartRate);
		}
		else
		{
			heartRate = Mathf.MoveTowards(heartRate, circulatorySystem.BloodInfo.HEARTRATE_NORMAL,
				(Time.time - lastHeartBeat) * heartRateDelta);
		}

		circulatorySystem.HeartRate = heartRate; //Let our circulatory system know what its heartrate is.
		//TODO: We may need to account for having multiple hearts here.
		//i.e. Instead of setting the heartrate of the circulatory system, we tell it to calcualte the average based on
		//individual pump events.

		heartBeatDelay = 60f / heartRate; //Time in seconds between heart beats.
	}

	public void DoHeartAttack()
	{
		HeartAttack = true;
	}
}