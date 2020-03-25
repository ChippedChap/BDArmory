using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Modules
{
	// Class for single use rearm. Extend this if you want to make multiple use rearm boxes.
	public class ModuleRearmAmmo : PartModule
	{
		[KSPField]
		public bool userToggleableInFlight = true;
		[KSPField]
		public bool userToggleableInEditor = true;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Ammo"), //Is Spare Reload:
			UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", scene = UI_Scene.All)]
		public bool ammoEnabled = false;

		private MissileFire weaponManagerCache;

		public MissileFire WeaponManager
		{
			get
			{
				if (weaponManagerCache && weaponManagerCache.vessel == vessel) return weaponManagerCache;
				weaponManagerCache = null;

				List<MissileFire>.Enumerator mf = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
				while (mf.MoveNext())
				{
					if (mf.Current == null) continue;
					weaponManagerCache = mf.Current;
					break;
				}
				mf.Dispose();
				return weaponManagerCache;
			}
		}

		public MissileLauncher Missile { get; private set; }

		public bool IsMissile { get; private set; } = false;

		public void Start()
		{
			UI_Toggle ammoToggleEditor = (UI_Toggle)Fields["ammoEnabled"].uiControlEditor;
			ammoToggleEditor.onFieldChanged = OnToggle;

			UI_Toggle ammoToggleFlight = (UI_Toggle)Fields["ammoEnabled"].uiControlFlight;
			ammoToggleFlight.onFieldChanged = OnToggle;

			Missile = part.GetComponent<MissileLauncher>();
			IsMissile = Missile;

			OnToggle(null, null, true);
		}

		public void Consume()
		{
			part.Die();
		}

		private void OnToggle(BaseField b, object o)
		{
			OnToggle(b, o, false);
		}

		private void OnToggle(BaseField b, object o, bool firstRun)
		{
			if (IsMissile)
			{
				// When user is not allowed to toggle during a scene - force it back
				// Dont do this when called from Start where firstRun should be true
				if(!firstRun)
				{
					if (HighLogic.LoadedSceneIsFlight && !userToggleableInFlight) ammoEnabled = !ammoEnabled;
					if (HighLogic.LoadedSceneIsEditor && !userToggleableInEditor) ammoEnabled = !ammoEnabled;
				}

				// Stop player from firing missiles when ammo is enabled.
				Missile.Actions["AGFire"].active = !ammoEnabled;
				Missile.Events["GuiFire"].active = !ammoEnabled;

				if (HighLogic.LoadedSceneIsFlight) WeaponManager.UpdateList();
			}
		}

		public static bool IsAmmo(Part p)
		{
			ModuleRearmAmmo ammo = p.FindModuleImplementing<ModuleRearmAmmo>();
			return ammo && ammo.ammoEnabled;
		}
	}
}
