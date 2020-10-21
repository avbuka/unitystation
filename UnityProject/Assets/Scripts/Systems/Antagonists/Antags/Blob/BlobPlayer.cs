﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DatabaseAPI;
using Light2D;
using Mirror;
using UnityEngine;
using TMPro;
using Random = UnityEngine.Random;

namespace Blob
{
	/// <summary>
	/// Class which has the logic and data for the blob player
	/// </summary>
	public class BlobPlayer : NetworkBehaviour
	{
		[SerializeField] private GameObject blobCorePrefab = null;
		[SerializeField] private GameObject blobNodePrefab = null;
		[SerializeField] private GameObject blobResourcePrefab = null;
		[SerializeField] private GameObject blobFactoryPrefab = null;
		[SerializeField] private GameObject blobReflectivePrefab = null;
		[SerializeField] private GameObject blobStrongPrefab = null;
		[SerializeField] private GameObject blobNormalPrefab = null;

		[SerializeField] private GameObject attackEffect = null;

		[SerializeField] private GameObject blobSpore = null;

		[SerializeField] private int normalBlobCost = 4;
		[SerializeField] private int strongBlobCost = 15;
		[SerializeField] private int reflectiveBlobCost = 15;
		[SerializeField] private int resourceBlobCost = 40;
		[SerializeField] private int nodeBlobCost = 50;
		[SerializeField] private int factoryBlobCost = 60;
		[SerializeField] private int moveCoreCost = 80;

		private int attackCost = 1;

		private float refundPercentage = 0.4f;

		public BlobVariants blobVariants;

		private GameObject blobCore;
		private Integrity coreHealth;
		private TMP_Text healthText;
		private TMP_Text resourceText;
		private TMP_Text numOfBlobTilesText;

		public int playerDamage = 20;
		public int objectDamage = 50;
		public int layerDamage = 40;
		public AttackType attackType = AttackType.Melee;
		public DamageType damageType = DamageType.Brute;

		private PlayerSync playerSync;
		private RegisterPlayer registerPlayer;
		private PlayerScript playerScript;
		public int numOfTilesForVictory = 400;
		private int numOfTilesForDetection = 75;

		private bool teleportCheck;
		private bool victory;
		private bool announcedBlob;
		private float announceAfterSeconds = 600f;
		private bool hasDied;
		private bool halfWay;
		private bool nearlyWon;

		[HideInInspector] public bool BlobRemoveMode;

		private float timer = 0f;
		private float econTimer = 0f;
		private float factoryTimer = 0f;
		private float healthTimer = 0f;

		[SerializeField]
		private float econModifier = 1f;

		[SerializeField]
		private int maxBiomass = 100;

		[SerializeField]
		[Tooltip("If true then there will be announcements when blob is close to destroying station, after the initial biohazard.")]
		private bool isBlobGamemode = true;

		[SerializeField]
		private bool endRoundWhenKilled = false;

		[SerializeField]
		private bool endRoundWhenBlobVictory = true;

		[SerializeField]
		private bool rapidExpand = false;

		[SerializeField]
		private int blobSpreadDistance = 4;

		[SerializeField]
		private int buildDistanceLimit = 4;

		[SerializeField]
		private GameObject overmindLightObject = null;

		[SerializeField]
		private LightSprite overmindLight = null;

		[SerializeField]
		private GameObject overmindSprite = null;

		public bool clickCoords = true;

		private ConcurrentDictionary<Vector3Int, BlobStructure> blobTiles =
			new ConcurrentDictionary<Vector3Int, BlobStructure>();

		private HashSet<GameObject> nonSpaceBlobTiles = new HashSet<GameObject>();

		private HashSet<GameObject> resourceBlobs = new HashSet<GameObject>();

		private ConcurrentDictionary<GameObject, HashSet<GameObject>> factoryBlobs =
			new ConcurrentDictionary<GameObject, HashSet<GameObject>>();

		private HashSet<BlobStructure> nodeBlobs = new HashSet<BlobStructure>();

		private List<Vector3Int> coords = new List<Vector3Int>
		{
			new Vector3Int(0, 0, 0),
			new Vector3Int(0, 1, 0),
			new Vector3Int(1, 0, 0),
			new Vector3Int(0, -1, 0),
			new Vector3Int(-1, 0, 0)
		};

		[SyncVar(hook = nameof(SyncResources))]
		private int resources = 0;

		[SyncVar(hook = nameof(SyncHealth))]
		private float health = 400;

		[SyncVar(hook = nameof(SyncNumOfBlobTiles))]
		private int numOfNonSpaceBlobTiles = 1;

		private int numOfBlobTiles = 1;

		private int maxCount = 0;

		private int maxNonSpaceCount = 0;

		private Color color = Color.green;//new Color(154, 205, 50);

		public int NumOfNonSpaceBlobTiles => numOfNonSpaceBlobTiles;

		/// <summary>
		/// The start function of the script called from BlobStarter when player turns into blob, sets up core.
		/// </summary>
		public void BlobStart()
		{
			playerSync = GetComponent<PlayerSync>();
			registerPlayer = GetComponent<RegisterPlayer>();
			playerScript = GetComponent<PlayerScript>();

			if (playerScript == null && (!TryGetComponent(out playerScript) || playerScript == null))
			{
				Debug.LogError("Playerscript was null on blob and couldnt be found.");
				return;
			}

			playerScript.mind.ghost = playerScript;
			playerScript.mind.body = playerScript;

			var name = $"Overmind {Random.Range(1, 1001)}";

			playerScript.characterSettings.Name = name;
			playerScript.playerName = name;

			playerScript.IsBlob = true;

			var result = Spawn.ServerPrefab(blobCorePrefab, playerSync.ServerPosition, gameObject.transform);

			if (!result.Successful)
			{
				Debug.LogError("Failed to spawn blob core for player!");
				return;
			}

			blobCore = result.GameObject;

			var pos = blobCore.GetComponent<CustomNetTransform>().ServerPosition;

			var structure = blobCore.GetComponent<BlobStructure>();

			blobTiles.TryAdd(pos, structure);

			structure.location = pos;
			SetLightAndColor(structure);

			//Make core act like node
			structure.expandCoords = GenerateCoords(pos);
			structure.healthPulseCoords = structure.expandCoords;
			nodeBlobs.Add(structure);

			//Set up death detection
			coreHealth = blobCore.GetComponent<Integrity>();
			coreHealth.OnWillDestroyServer.AddListener(Death);

			//Block escape shuttle from leaving station when it arrives
			GameManager.Instance.PrimaryEscapeShuttle.SetHostileEnvironment(true);

			TargetRpcTurnOnClientLight(connectionToClient);
		}

		private void OnEnable()
		{
			UpdateManager.Add(PeriodicUpdate, 1f);

			var uiBlob = UIManager.Display.hudBottomBlob.GetComponent<UI_Blob>();
			uiBlob.blobPlayer = this;
			uiBlob.controller = GetComponent<BlobMouseInputController>();
			healthText = uiBlob.healthText;
			resourceText = uiBlob.resourceText;
			numOfBlobTilesText = uiBlob.numOfBlobTilesText;
		}

		private void OnDisable()
		{
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, PeriodicUpdate);
		}

		private void PeriodicUpdate()
		{
			if (!CustomNetworkManager.IsServer) return;

			timer += 1f;
			econTimer += 1f;
			factoryTimer += 1f;
			healthTimer += 1f;

			//Force overmind back to blob if camera moves too far
			if (!teleportCheck && !victory && !ValidateAction(playerSync.ServerPosition, true) && blobCore != null)
			{
				teleportCheck = true;

				StartCoroutine(TeleportPlayerBack());
			}

			//Count number of blob tiles
			numOfNonSpaceBlobTiles = nonSpaceBlobTiles.Count;
			numOfBlobTiles = blobTiles.Count;

			if (numOfBlobTiles > maxCount)
			{
				maxCount = numOfBlobTiles;
			}

			if (numOfNonSpaceBlobTiles > maxNonSpaceCount)
			{
				maxNonSpaceCount = numOfNonSpaceBlobTiles;
			}

			if (isBlobGamemode && !halfWay && numOfNonSpaceBlobTiles >= numOfTilesForVictory / 2)
			{
				halfWay = true;

				Chat.AddSystemMsgToChat(
					string.Format(CentComm.BioHazardReportTemplate,
						"Caution! Biohazard expanding rapidly. Station structural integrity failing."),
					MatrixManager.MainStationMatrix);
				SoundManager.PlayNetworked("Notice1");
			}

			if (isBlobGamemode && !nearlyWon && numOfNonSpaceBlobTiles >= numOfTilesForVictory / 1.25)
			{
				nearlyWon = true;

				Chat.AddSystemMsgToChat(
					string.Format(CentComm.BioHazardReportTemplate,
						"Alert! Station integrity near critical. Biomass sensor levels are off the charts."),
					MatrixManager.MainStationMatrix);
				SoundManager.PlayNetworked("Notice1");
			}

			//Blob wins after number of blob tiles reached
			if (!victory && numOfNonSpaceBlobTiles >= numOfTilesForVictory)
			{
				victory = true;
				BlobWins();
			}

			//Detection check
			if (!announcedBlob && (timer >= announceAfterSeconds || numOfBlobTiles >= numOfTilesForDetection))
			{
				announcedBlob = true;

				Chat.AddSystemMsgToChat(
					string.Format(CentComm.BioHazardReportTemplate,
						"Confirmed outbreak of level 5 biohazard aboard the station. All personnel must contain the outbreak."),
					MatrixManager.MainStationMatrix);
				SoundManager.PlayNetworked("Outbreak5");
			}

			BiomassTick();

			TrySpawnSpores();

			AutoExpandBlob();

			HealthPulse();

			if (blobCore == null) return;

			health = coreHealth.integrity;
		}

		[TargetRpc]
		private void TargetRpcTurnOnClientLight(NetworkConnection target)
		{
			overmindLightObject.SetActive(true);
			overmindLight.Color = color;
			overmindLight.Color.a = 0.2f;
			overmindSprite.layer = 29;
			playerScript.IsBlob = true;
		}

		[TargetRpc]
		private void TargetRpcTurnOffBlob(NetworkConnection target)
		{
			playerScript.IsBlob = false;
		}

		#region teleport

		private IEnumerator TeleportPlayerBack()
		{
			Chat.AddExamineMsgFromServer(gameObject,
				"You feel lost without blob.<color=#FF151F>Move back to the blob</color>");

			yield return WaitFor.Seconds(1f);

			if (ValidateAction(playerSync.ServerPosition, true))
			{
				teleportCheck = false;
				yield break;
			}

			Chat.AddExamineMsgFromServer(gameObject, "Your mind gets sucked back to the blob");

			if(blobCore == null) yield break;

			TeleportToNode();

			teleportCheck = false;
		}

		[Command]
		public void CmdTeleportToCore()
		{
			if(blobCore == null) return;

			playerSync.SetPosition(blobCore.WorldPosServer());
		}

		[Command]
		public void CmdTeleportToNode()
		{
			TeleportToNode();
		}

		private void TeleportToNode()
		{
			GameObject node = null;

			var vector = float.PositiveInfinity;

			var pos = playerSync.ServerPosition;

			//Find closet node
			foreach (var blobStructure in nodeBlobs)
			{
				var distance = Vector3.Distance(blobStructure.location, pos);

				if (distance > vector) continue;

				vector = distance;
				node = blobStructure.gameObject;
			}

			//If null go to core instead
			if (node == null)
			{
				//Blob is dead :(
				if(blobCore == null) return;

				playerSync.SetPosition(blobCore.WorldPosServer());
				return;
			}

			playerSync.SetPosition(node.WorldPosServer());
		}

		#endregion

		#region SyncVars

		private void SyncResources(int oldVar, int newVar)
		{
			resources = newVar;
			resourceText.text = newVar.ToString();
		}

		private void SyncHealth(float oldVar, float newVar)
		{
			health = newVar;
			healthText.text = newVar.ToString();
		}

		private void SyncNumOfBlobTiles(int oldVar, int newVar)
		{
			numOfNonSpaceBlobTiles = newVar;
			numOfBlobTilesText.text = newVar.ToString();
		}

		#endregion

		#region Manual Placing and attacking

		/// <summary>
		/// Manually placing or attacking
		/// </summary>
		/// <param name="worldPos"></param>
		[Command]
		public void CmdTryPlaceBlobOrAttack(Vector3Int worldPos)
		{
			worldPos.z = 0;

			//Whether player can click anywhere, or if when they click it treats it as if they clicked the tile they're
			//standing on (or around since validation checks adjacent)
			if (!clickCoords)
			{
				worldPos = playerSync.ServerPosition;
			}

			//Whether player has toggled always remove on in the UI
			if (BlobRemoveMode)
			{
				InternalRemoveBlob(worldPos);
				return;
			}

			PlaceBlobOrAttack(worldPos);
		}

		private bool PlaceBlobOrAttack(Vector3Int worldPos, bool autoExpanding = false)
		{
			if (!ValidateAction(worldPos)) return false;

			if (!autoExpanding && resources < attackCost)
			{
				Chat.AddExamineMsgFromServer(gameObject,
					$"Not enough biomass to attack, you need {attackCost} biomass");
				return false;
			}

			if (!autoExpanding && Cooldowns.IsOn(playerScript, CooldownID.Asset(CommonCooldowns.Instance.Melee, NetworkSide.Server)))
			{
				//On attack cooldown
				return false;
			}

			if (TryAttack(worldPos))
			{
				if (autoExpanding)
				{
					return false;
				}

				if (!victory)
				{
					Cooldowns.TryStartServer(playerScript, CommonCooldowns.Instance.Melee);
				}

				resources -= attackCost;
				return true;
			}

			//See if theres blob already there
			if (blobTiles.ContainsKey(worldPos))
			{
				if (blobTiles.TryGetValue(worldPos, out var blob) && blob != null)
				{
					//Cant place normal blob where theres normal blob
					return true;
				}

				SpawnNormalBlob(worldPos, autoExpanding);
				return false;
			}

			//If blob doesnt exist at that location already try placing
			SpawnNormalBlob(worldPos, autoExpanding, true);

			return false;
		}

		private void SpawnNormalBlob(Vector3Int worldPos, bool autoExpanding, bool newPosition = false)
		{
			if (!autoExpanding && !ValidateCost(normalBlobCost, blobNormalPrefab)) return;

			var result = Spawn.ServerPrefab(blobNormalPrefab, worldPos, gameObject.transform);

			if (!result.Successful) return;

			if (!autoExpanding)
			{
				Chat.AddExamineMsgFromServer(gameObject, "You grow a normal blob.");
			}

			var structure = result.GameObject.GetComponent<BlobStructure>();

			structure.location = worldPos;
			SetLightAndColor(structure);

			AddNonSpaceBlob(result.GameObject);

			if (newPosition)
			{
				blobTiles.TryAdd(worldPos, structure);
				return;
			}

			blobTiles[worldPos] = structure;
		}

		private bool TryAttack(Vector3 worldPos)
		{
			var matrix = registerPlayer.Matrix;

			if (matrix == null)
			{
				Debug.LogError("matrix for blob click was null");
				return false;
			}

			var metaTileMap = matrix.MetaTileMap;

			var pos = worldPos.RoundToInt();

			var local = worldPos.ToLocalInt(matrix);

			var players = matrix.Get<LivingHealthBehaviour>(local, ObjectType.Player, true);

			foreach (var player in players)
			{
				if(player.IsDead) continue;

				player.ApplyDamage(gameObject, playerDamage, attackType, damageType);

				Chat.AddAttackMsgToChat(gameObject, player.gameObject, customAttackVerb: "tried to absorb");

				PlayAttackEffect(pos);

				return true;
			}

			var hits = matrix.Get<RegisterTile>(local, ObjectType.Object, true)
				.Where( hit => hit != null && !hit.IsPassable(true) && hit.GetComponent<BlobStructure>() == null);

			foreach (var hit in hits)
			{
				//Try damage NPC
				if (hit.TryGetComponent<LivingHealthBehaviour>(out var npcComponent))
				{
					if(npcComponent.IsDead) continue;

					npcComponent.ApplyDamage(gameObject, playerDamage, attackType, damageType);

					Chat.AddAttackMsgToChat(gameObject, hit.gameObject, customAttackVerb: "tried to absorb");

					PlayAttackEffect(pos);

					return true;
				}

				//Dont bother destroying passable stuff, eg open door
				if (hit.IsPassable(true)) continue;

				if (hit.TryGetComponent<Integrity>(out var component) && !component.Resistances.Indestructable)
				{
					component.ApplyDamage(objectDamage, attackType, damageType, true);

					Chat.AddLocalMsgToChat($"The blob attacks the {hit.gameObject.ExpensiveName()}", gameObject);

					PlayAttackEffect(pos);

					return true;
				}
			}

			//Do check to see if the impassable thing is a friendly blob, as it will be the only object left
			var hitsSecond = matrix.Get<RegisterTile>(local, ObjectType.Object, true)
				.Where(hit => hit != null && hit.GetComponent<BlobStructure>() != null);

			if (hitsSecond.Any())
			{
				return false;
			}

			//Check for walls, windows and grills
			if (metaTileMap != null && !MatrixManager.IsPassableAt(pos, true))
			{
				//Cell pos is unused var
				metaTileMap.ApplyDamage(Vector3Int.zero, layerDamage,
					pos, attackType);

				PlayAttackEffect(pos);

				return true;
			}

			return false;
		}

		#endregion

		#region Validation

		/// <summary>
		/// Validate that the action can only happen adjacent to existing blob
		/// </summary>
		/// <param name="worldPos"></param>
		/// <returns></returns>
		private bool ValidateAction(Vector3 worldPos, bool noMsg = false)
		{
			var pos = worldPos.RoundToInt();

			foreach (var offSet in coords)
			{
				if (blobTiles.ContainsKey(pos + offSet))
				{
					return true;
				}
			}

			if (noMsg) return false;

			//No adjacent blobs, therefore cannot attack or expand
			Chat.AddExamineMsgFromServer(gameObject, "Can only place blob on or next to existing blob growth.");

			return false;
		}

		/// <summary>
		/// Validate cost, make sure have enough resources
		/// </summary>
		/// <param name="cost"></param>
		/// <param name="toSpawn"></param>
		/// <returns></returns>
		private bool ValidateCost(int cost, GameObject toSpawn, bool noMsg = false)
		{
			if (resources >= cost)
			{
				resources -= cost;
				return true;
			}

			if (noMsg) return false;

			Chat.AddExamineMsgFromServer(gameObject,
				$"Unable to place {toSpawn.ExpensiveName()}, {cost - resources} biomass missing");

			return false;
		}

		/// <summary>
		/// Validate the distance between specialised blobs
		/// </summary>
		/// <param name="worldPos"></param>
		/// <returns></returns>
		private bool ValidateDistance(Vector3Int worldPos, bool excludeNodes = false, bool excludeFactory = false,
			bool excludeResource = false)
		{
			var specialisedBlobs = new List<Vector3Int>();

			if (!excludeNodes)
			{
				foreach (var node in nodeBlobs)
				{
					if(node == null) continue;

					specialisedBlobs.Add(node.location);
				}
			}

			if (!excludeFactory)
			{
				foreach (var factory in factoryBlobs)
				{
					if(factory.Key == null) continue;

					specialisedBlobs.Add(factory.Key.GetComponent<BlobStructure>().location);
				}
			}

			if (!excludeResource)
			{
				foreach (var resourceBlob in resourceBlobs)
				{
					if(resourceBlob == null) continue;

					specialisedBlobs.Add(resourceBlob.GetComponent<BlobStructure>().location);
				}
			}

			foreach (var blob in specialisedBlobs)
			{
				if (Vector3Int.Distance(blob, worldPos) <= buildDistanceLimit)
				{
					//A blob is too close
					return false;
				}
			}

			return true;
		}

		#endregion

		#region Effects

		private void PlayAttackEffect(Vector3 worldPos)
		{
			var result = Spawn.ServerPrefab(attackEffect, worldPos, gameObject.transform.parent);

			if (!result.Successful) return;

			StartCoroutine(DespawnEffect(result.GameObject));
		}

		private IEnumerator DespawnEffect(GameObject effect)
		{
			yield return WaitFor.Seconds(1f);

			Despawn.ServerSingle(effect);
		}

		#endregion

		#region PlaceStrong/Reflective

		[Command]
		public void CmdTryPlaceStrongReflective(Vector3Int worldPos)
		{
			worldPos.z = 0;

			if (!ValidateAction(worldPos)) return;

			if (blobTiles.TryGetValue(worldPos, out var blob) && blob != null)
			{
				//Try place strong
				if (blob != null && blob.isNormal)
				{
					PlaceStrongReflective(blobStrongPrefab, blob, strongBlobCost, worldPos, "You grow a strong blob, you can now mutate to a reflective.");
					return;
				}

				//Try place reflective
				if (blob != null && blob.isStrong)
				{
					PlaceStrongReflective(blobReflectivePrefab, blob, reflectiveBlobCost, worldPos, "You grow a reflective blob.");
					return;
				}
			}

			//No normal blob at tile
			Chat.AddExamineMsgFromServer(gameObject, "You need to place a normal blob first");
		}

		private void PlaceStrongReflective(GameObject prefab, BlobStructure originalBlob, int cost, Vector3Int worldPos, string msg)
		{
			if (!ValidateCost(cost, prefab)) return;

			var result = Spawn.ServerPrefab(prefab, worldPos, gameObject.transform);

			if (!result.Successful) return;

			Chat.AddExamineMsgFromServer(gameObject, msg);

			Despawn.ServerSingle(originalBlob.gameObject);
			var structure = result.GameObject.GetComponent<BlobStructure>();
			SetLightAndColor(structure);
			blobTiles[worldPos] = structure;
			AddNonSpaceBlob(result.GameObject);
		}

		#endregion

		#region PlaceOther

		[Command]
		public void CmdTryPlaceOther(Vector3Int worldPos, BlobConstructs blobConstructs)
		{
			worldPos.z = 0;

			if (!ValidateAction(worldPos)) return;

			GameObject prefab = null;

			var cost = 0;

			switch (blobConstructs)
			{
				case BlobConstructs.Node:
					prefab = blobNodePrefab;
					cost = nodeBlobCost;
					break;
				case BlobConstructs.Factory:
					prefab = blobFactoryPrefab;
					cost = factoryBlobCost;
					break;
				case BlobConstructs.Resource:
					prefab = blobResourcePrefab;
					cost = resourceBlobCost;
					break;
				default:
					Debug.LogError("Switch has no correct case for blob structure!");
					break;
			}

			if (prefab == null) return;

			if (blobTiles.TryGetValue(worldPos, out var blob) && blob != null)
			{
				if (blob != null && blob.isNormal)
				{
					if (blobConstructs != BlobConstructs.Node && !ValidateDistance(worldPos, true))
					{
						Chat.AddExamineMsgFromServer(gameObject, $"Too close to another factory or resource blob, place at least {buildDistanceLimit}m away");
						return;
					}

					if (blobConstructs == BlobConstructs.Node && !ValidateDistance(worldPos, excludeFactory: true, excludeResource: true))
					{
						Chat.AddExamineMsgFromServer(gameObject, $"Too close to another node, place at least {buildDistanceLimit}m away");
						return;
					}

					if (!ValidateCost(cost, prefab)) return;

					var result = Spawn.ServerPrefab(prefab, worldPos, gameObject.transform);

					if (!result.Successful) return;

					Chat.AddExamineMsgFromServer(gameObject, $"You grow a {blobConstructs} blob.");

					var structure = result.GameObject.GetComponent<BlobStructure>();

					switch (blobConstructs)
					{
						case BlobConstructs.Node:

							structure.expandCoords = GenerateCoords(worldPos);
							structure.healthPulseCoords = structure.expandCoords;
							nodeBlobs.Add(structure);
							break;
						case BlobConstructs.Factory:
							factoryBlobs.TryAdd(result.GameObject, new HashSet<GameObject>());
							break;
						case BlobConstructs.Resource:
							resourceBlobs.Add(result.GameObject);
							break;
						default:
							Debug.LogError("Switch has no correct case for blob structure!");
							break;
					}

					Despawn.ServerSingle(blob.gameObject);

					structure.location = worldPos;
					SetLightAndColor(structure);

					blobTiles[worldPos] = structure;
					AddNonSpaceBlob(result.GameObject);
					return;
				}
			}

			//No normal blob at tile
			Chat.AddExamineMsgFromServer(gameObject, "You need to place a normal blob first");
		}

		#endregion

		#region RemoveBlob

		[Command]
		public void CmdRemoveBlob(Vector3Int worldPos)
		{
			worldPos.z = 0;

			InternalRemoveBlob(worldPos);
		}

		private void InternalRemoveBlob(Vector3Int worldPos)
		{
			if (!blobTiles.TryGetValue(worldPos, out var blob) || blob == null)
			{
				Chat.AddExamineMsgFromServer(gameObject, "No blob to be removed");
			}
			else
			{
				if (blob.isNode || blob.isCore)
				{
					Chat.AddExamineMsgFromServer(gameObject, "This is a core or node blob. It cannot be removed");
					return;
				}

				var returnCost = 0;

				if (blob.isNormal)
				{
					returnCost = Mathf.RoundToInt(normalBlobCost * refundPercentage);
				}
				else if (blob.isStrong)
				{
					returnCost = Mathf.RoundToInt(strongBlobCost * refundPercentage);
				}
				else if (blob.isReflective)
				{
					returnCost = Mathf.RoundToInt(reflectiveBlobCost * refundPercentage);
				}
				else if (blob.isResource)
				{
					returnCost = Mathf.RoundToInt(resourceBlobCost * refundPercentage);
				}
				else if (blob.isFactory)
				{
					returnCost = Mathf.RoundToInt(factoryBlobCost * refundPercentage);
				}

				Chat.AddExamineMsgFromServer(gameObject, $"Blob removed, {AddToResources(returnCost)} biomass refunded");

				Despawn.ServerSingle(blob.gameObject);
				blobTiles.TryRemove(worldPos, out var empty);
			}
		}

		/// <summary>
		/// Toggle Remove when clicking, not just Alt clicking
		/// </summary>
		[Command]
		public void CmdToggleRemove(bool forceOff)
		{
			if (forceOff)
			{
				BlobRemoveMode = false;
				return;
			}

			BlobRemoveMode = !BlobRemoveMode;
		}

		#endregion

		#region Death

		public void Death(DestructionInfo info = null)
		{
			if(!CustomNetworkManager.IsServer || hasDied) return;

			hasDied = true;

			coreHealth.OnWillDestroyServer.RemoveListener(Death);

			//Destroy all blob tiles
			foreach (var tile in blobTiles)
			{
				if (tile.Value != null)
				{
					Despawn.ServerSingle(tile.Value.gameObject);
				}
			}

			blobTiles = new ConcurrentDictionary<Vector3Int, BlobStructure>();

			GameManager.Instance.PrimaryEscapeShuttle.SetHostileEnvironment(false);

			GameManager.Instance.CentComm.ChangeAlertLevel(CentComm.AlertLevel.Blue);

			Chat.AddSystemMsgToChat(
				string.Format(CentComm.BioHazardReportTemplate,
					"The biohazard has been contained."),
				MatrixManager.MainStationMatrix);

			playerScript.IsBlob = false;
			TargetRpcTurnOffBlob(connectionToClient);

			//Make blob into ghost
			PlayerSpawn.ServerSpawnGhost(playerScript.mind);

			if (endRoundWhenKilled)
			{
				GameManager.Instance.EndRound();
			}

			Destroy(this);
		}

		#endregion

		#region Victory

		private void BlobWins()
		{
			GameManager.Instance.CentComm.ChangeAlertLevel(CentComm.AlertLevel.Delta);

			Chat.AddSystemMsgToChat(
				string.Format(CentComm.BioHazardReportTemplate,
					"Biohazard has reached critical mass. Station integrity critical!"),
				MatrixManager.MainStationMatrix);

			Chat.AddExamineMsgFromServer(gameObject, "Your hunger is unstoppable, you are fully unleashed");

			maxBiomass = 5000;

			econModifier = 10f;

			rapidExpand = true;

			foreach (var objective in playerScript.mind.GetAntag().Objectives)
			{
				objective.SetAsComplete();
			}

			if (endRoundWhenBlobVictory)
			{
				StartCoroutine(EndRound());
			}
		}

		private IEnumerator EndRound()
		{
			yield return WaitFor.Seconds(180f);

			Chat.AddGameWideSystemMsgToChat("The blob has consumed the station, we are all but goo now.");

			Chat.AddGameWideSystemMsgToChat($"At its biggest the blob had {maxCount} tiles controlled" +
			                                $" but only had {maxNonSpaceCount} non-space tiles which counted to victory.");

			GameManager.Instance.EndRound();
		}

		#endregion

		#region SwitchCore

		[Command]
		public void CmdMoveCore(Vector3Int worldPos)
		{
			worldPos.z = 0;

			if (blobTiles.TryGetValue(worldPos, out var blob) && blob != null)
			{
				SwitchCore(blob);
			}
		}

		/// <summary>
		/// Switches a node into a core
		/// </summary>
		/// <param name="oldNode"></param>
		public void SwitchCore(BlobStructure oldNode)
		{
			if (!oldNode.isNode)
			{
				Chat.AddExamineMsgFromServer(gameObject, "Can only move the core to a node");
				return;
			}

			if (!ValidateCost(moveCoreCost, blobCorePrefab, true))
			{
				Chat.AddExamineMsgFromServer(gameObject,
					$"Not enough biomass to move core, {moveCoreCost - resources} biomass missing");
				return;
			}

			var core = blobCore.GetComponent<CustomNetTransform>();
			var node = oldNode.GetComponent<CustomNetTransform>();

			var coreCache = core.ServerPosition;

			core.SetPosition(node.ServerPosition);
			node.SetPosition(coreCache);

			ResetArea(blobCore);
			ResetArea(oldNode.gameObject);
		}

		#endregion

		#region AutoExpand

		private void AutoExpandBlob()
		{
			//Node auto expand logic
			foreach (var node in nodeBlobs.Shuffle())
			{
				if(node == null || node.nodeDepleted) continue;

				var coordsLeft = node.expandCoords;

				foreach (var expandCoord in coordsLeft.Shuffle())
				{
					if (!ValidateAction(expandCoord.To3Int(), true))
					{
						continue;
					}

					if (PlaceBlobOrAttack(expandCoord.To3Int(), true))
					{
						node.expandCoords.Remove(expandCoord);
						continue;
					}

					if(rapidExpand) continue;
					break;
				}

				if (node.expandCoords.Count == 0)
				{
					node.nodeDepleted = true;
				}
			}
		}

		private void ResetArea(GameObject node)
		{
			var pos = node.GetComponent<CustomNetTransform>().ServerPosition;
			var structNode = node.GetComponent<BlobStructure>();
			structNode.expandCoords = GenerateCoords(pos);
			structNode.healthPulseCoords = structNode.expandCoords;
			structNode.location = pos;
			structNode.nodeDepleted = false;
		}

		private List<Vector2Int> GenerateCoords(Vector3Int worldPos)
		{
			int r2 = blobSpreadDistance * blobSpreadDistance;
			int area = r2 << 2;
			int rr = blobSpreadDistance << 1;

			var data = new List<Vector2Int>();

			for (int i = 0; i < area; i++)
			{
				int tx = (i % rr) - blobSpreadDistance;
				int ty = (i / rr) - blobSpreadDistance;

				if (tx * tx + ty * ty <= r2)
					data.Add(new Vector2Int(worldPos.x + tx, worldPos.y + ty));
			}

			return data;
		}

		#endregion

		#region FactoryBlob

		private void TrySpawnSpores()
		{
			if (factoryTimer < 10f) return;

			factoryTimer = 0f;

			foreach (var factoryBlob in factoryBlobs)
			{
				if (factoryBlob.Key == null) continue;

				factoryBlob.Value.Remove(null);

				var spores = factoryBlob.Value;

				foreach (var spore in spores)
				{
					if (spore.GetComponent<LivingHealthBehaviour>().IsDead)
					{
						factoryBlob.Value.Remove(spore);
					}
				}

				//Create max of three spore
				if (factoryBlob.Value.Count >= 3) continue;

				var result = Spawn.ServerPrefab(blobSpore, factoryBlob.Key.WorldPosServer(),
					factoryBlob.Key.transform);

				if (!result.Successful) continue;

				factoryBlob.Value.Add(result.GameObject);
			}
		}

		#endregion

		#region Economy

		private void BiomassTick()
		{
			//Gain biomass every 5 seconds
			if (econTimer >= 5f)
			{
				econTimer = 0f;

				//Remove null if possible
				resourceBlobs.Remove(null);

				//One biomass for each resource node
				var newBiomass = Mathf.RoundToInt((resourceBlobs.Count + 3) * econModifier); //Base income of three

				AddToResources(newBiomass);
			}
		}

		private int AddToResources(int newBiomass)
		{
			var used = 0;

			//Reset to max if over
			if (resources >= maxBiomass)
			{
				resources = maxBiomass;
				return maxBiomass;
			}
			//Add only to max if it would go above max
			else if (resources + newBiomass > maxBiomass)
			{
				used = maxBiomass - resources;
			}
			else
			{
				used = newBiomass;
			}

			resources += used;
			return used;
		}

		#endregion

		#region Light/Color

		private void SetLightAndColor(BlobStructure blobStructure)
		{
			if (blobStructure.lightSprite != null)
			{
				blobStructure.lightSprite.Color = color;
				blobStructure.lightSprite.Color.a = 0.2f;
			}

			blobStructure.spriteHandler.SetColor(color);
		}

		#endregion

		#region CountSpace

		private void AddNonSpaceBlob(GameObject newBlob)
		{
			if(MatrixManager.IsSpaceAt(newBlob.GetComponent<RegisterObject>().WorldPositionServer, true)) return;

			nonSpaceBlobTiles.Remove(null);
			nonSpaceBlobTiles.Add(newBlob);
		}

		#endregion

		#region HealthPulse

		private void HealthPulse()
		{
			if(healthTimer <= 10f) return;

			healthTimer = 0f;

			foreach (var node in nodeBlobs)
			{
				if(node == null) continue;

				foreach (var healthPulseTarget in node.healthPulseCoords)
				{
					if (blobTiles.TryGetValue(healthPulseTarget.To3Int(), out var blob) && blob != null)
					{
						if(blob.integrity == null) continue;

						blob.integrity.RestoreIntegrity(node.isCore ? 3 : 1);
					}
				}
			}
		}

		#endregion
	}

	public enum BlobConstructs
	{
		Core,
		Node,
		Resource,
		Factory,
		Strong,
		Reflective
	}

	public class BlobVariants
	{
		public string name;

		public List<Damages> damages = new List<Damages>();

		public List<Resistances> resistanceses = new List<Resistances>();

		public Color color;
	}

	[Serializable]
	public class Damages
	{
		public int damageDone;

		public DamageType damageType;
	}
}