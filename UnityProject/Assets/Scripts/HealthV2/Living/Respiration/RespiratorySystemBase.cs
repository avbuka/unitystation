﻿using System;
using System.Collections;
using System.Collections.Generic;
using Atmospherics;
using Objects.GasContainer;
using UnityEngine;
using Random = UnityEngine.Random;

namespace HealthV2
{
	[RequireComponent(typeof(LivingHealthMasterBase))]
	[RequireComponent(typeof(CirculatorySystemBase))]
	public class RespiratorySystemBase : MonoBehaviour
	{

		[SerializeField]
		private RespiratoryInfo respiratoryInfo;
		public RespiratoryInfo RespiratoryInfo => respiratoryInfo;

		private LivingHealthMasterBase healthMaster;
		private PlayerScript playerScript;
		private RegisterTile registerTile;
		private Equipment equipment;
		private ObjectBehaviour objectBehaviour;

		//If the organism breathes, it needs a way to circulate that.
		private CirculatorySystemBase circulatorySystem;

		[SerializeField]
		[Tooltip("If this is turned on, the organism can breathe anyway and wont effect atmospherics.")]
		private bool canBreathAnywhere = false;

		[SerializeField]
		private float tickRate = 1f;

		public bool IsSuffocating;
		public float temperature = 293.15f;
		public float pressure = 101.325f;
		private float breatheCooldown = 0;

		private void Awake()
		{
			circulatorySystem = GetComponent<CirculatorySystemBase>();
			healthMaster = GetComponent<LivingHealthMasterBase>();
			playerScript = GetComponent<PlayerScript>();
			registerTile = GetComponent<RegisterTile>();
			equipment = GetComponent<Equipment>();
			objectBehaviour = GetComponent<ObjectBehaviour>();
		}

		void OnEnable()
		{
			UpdateManager.Add(UpdateMe, tickRate);
		}

		void OnDisable()
		{
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateMe);
		}

		//Handle by UpdateManager
		void UpdateMe()
		{
			//Server Only:
			if (CustomNetworkManager.IsServer && MatrixManager.IsInitialized
			                                  && !canBreathAnywhere)
			{
				MonitorSystem();
			}
		}

		private void MonitorSystem()
		{
			if (!healthMaster.IsDead)
			{
				Vector3Int position = objectBehaviour.AssumedWorldPositionServer();
				MetaDataNode node = MatrixManager.GetMetaDataAt(position);

				if (!IsEVACompatible())
				{
					temperature = node.GasMix.Temperature;
					pressure = node.GasMix.Pressure;
					CheckPressureDamage();
				}
				else
				{
					pressure = 101.325f;
					temperature = 293.15f;
				}

				if(healthMaster.OverallHealth >= HealthThreshold.SoftCrit){
					if (Breathe(node))
					{
						AtmosManager.Update(node);
					}
				}
			}
		}

		private bool Breathe(IGasMixContainer node)
		{
			breatheCooldown --; //not timebased, but tickbased
			if(breatheCooldown > 0){
				return false;
			}
			// if no internal breathing is possible, get the from the surroundings
			IGasMixContainer container = GetInternalGasMix() ?? node;

			GasMix gasMix = container.GasMix;
			GasMix breathGasMix = gasMix.RemoveVolume(AtmosConstants.BREATH_VOLUME, true);

			float gasUsed = HandleBreathing(breathGasMix);

			if (gasUsed > 0)
			{
				breathGasMix.RemoveGas(respiratoryInfo.RequiredGas, gasUsed);
				node.GasMix.AddGas(respiratoryInfo.ReleasedGas, gasUsed);
				registerTile.Matrix.MetaDataLayer.UpdateSystemsAt(registerTile.LocalPositionClient, SystemType.AtmosSystem);
			}

			gasMix += breathGasMix;
			container.GasMix = gasMix;

			return gasUsed > 0;
		}

		private GasContainer GetInternalGasMix()
		{
			if (playerScript != null)
			{

				// Check if internals exist
				var maskItemAttrs = playerScript.ItemStorage.GetNamedItemSlot(NamedSlot.mask).ItemAttributes;
				bool internalsEnabled = equipment.IsInternalsEnabled;
				if (maskItemAttrs != null && maskItemAttrs.CanConnectToTank && internalsEnabled)
				{
					foreach ( var gasSlot in playerScript.ItemStorage.GetGasSlots() )
					{
						if (gasSlot.Item == null) continue;
						var gasContainer = gasSlot.Item.GetComponent<GasContainer>();
						if ( gasContainer )
						{
							return gasContainer;
						}
					}
				}
			}

			return null;
		}

		private float HandleBreathing(GasMix breathGasMix)
		{
			float gasPressure = breathGasMix.GetPressure(respiratoryInfo.RequiredGas);

			float gasUsed = 0;

			if (gasPressure < respiratoryInfo.MinimumSafePressure)
			{
				if (Random.value < 0.1)
				{
					Chat.AddActionMsgToChat(gameObject, "You gasp for breath", $"{gameObject.name} gasps");
				}

				if (gasPressure > 0)
				{
					float ratio = 1 - gasPressure / respiratoryInfo.MinimumSafePressure;
					gasUsed = breathGasMix.GetMoles(respiratoryInfo.RequiredGas) * ratio;
				}
				IsSuffocating = true;
			}
			else
			{
				gasUsed = breathGasMix.GetMoles(respiratoryInfo.RequiredGas);
				IsSuffocating = false;
				breatheCooldown = respiratoryInfo.breathCooldown;
			}
			return gasUsed;
		}

		private void CheckPressureDamage()
		{
			if (pressure < AtmosConstants.MINIMUM_OXYGEN_PRESSURE)
			{
				ApplyDamage(AtmosConstants.LOW_PRESSURE_DAMAGE, DamageType.Brute);
			}
			else if (pressure > AtmosConstants.HAZARD_HIGH_PRESSURE)
			{
				float damage = Mathf.Min(((pressure / AtmosConstants.HAZARD_HIGH_PRESSURE) - 1) * AtmosConstants.PRESSURE_DAMAGE_COEFFICIENT,
					AtmosConstants.MAX_HIGH_PRESSURE_DAMAGE);

				ApplyDamage(damage, DamageType.Brute);
			}
		}

		private bool IsEVACompatible()
		{
			if (playerScript == null)
			{
				return false;
			}

			ItemAttributesV2 headItem = playerScript.ItemStorage.GetNamedItemSlot(NamedSlot.head).ItemAttributes;
			ItemAttributesV2 suitItem = playerScript.ItemStorage.GetNamedItemSlot(NamedSlot.outerwear).ItemAttributes;

			if (headItem != null && suitItem != null)
			{
				return headItem.IsEVACapable && suitItem.IsEVACapable;
			}

			return false;
		}

		private void ApplyDamage(float amount, DamageType damageType)
		{
			//TODO: Figure out what kind of damage low pressure should be doing.
			//healthMaster.ApplyDamage(null, amount, AttackType.Internal, damageType);
		}
	}

}
