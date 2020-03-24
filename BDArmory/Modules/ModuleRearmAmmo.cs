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

		[KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_Ammo"), //Is Spare Reload:
			UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", scene = UI_Scene.All)]
		public bool ammoEnabled = false;

		private MissileLauncher missile;

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

		public bool IsMissile { get; private set; } = false;

		public void Start()
		{
			Fields["ammoEnabled"].guiActive = userToggleableInFlight;
			Fields["ammoEnabled"].guiActiveEditor = userToggleableInEditor;

			UI_Toggle ammoToggle = (UI_Toggle)Fields["ammoEnabled"].uiControlEditor;
			ammoToggle.onFieldChanged = OnToggle;
			UI_Toggle ammoToggleFlight = (UI_Toggle)Fields["ammoEnabled"].uiControlFlight;
			ammoToggleFlight.onFieldChanged = OnToggle;

			missile = part.GetComponent<MissileLauncher>();
			IsMissile = missile;
		}

		public void Consume()
		{
			part.Die();
		}

		private void OnToggle(BaseField b, object o)
		{
			if (IsMissile)
			{
				// Stop player from firing missiles when ammo is enabled.
				missile.Actions["AGFire"].active = !ammoEnabled;
				missile.Events["GuiFire"].active = !ammoEnabled;

				if (HighLogic.LoadedSceneIsFlight) WeaponManager.UpdateList();
			}
		}
	}
}
