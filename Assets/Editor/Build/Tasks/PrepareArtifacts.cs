using System.IO;
using OwlcatModification.Editor.Build.Context;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Injector;
using UnityEditor.Build.Pipeline.Interfaces;

namespace OwlcatModification.Editor.Build.Tasks
{
	public class PrepareArtifacts : IBuildTask
	{
#pragma warning disable 649
		[InjectContext(ContextUsage.In)]
		private IBuildParameters m_BuildParameters;

		[InjectContext(ContextUsage.In)]
		private IModificationParameters m_ModificationParameters;
#pragma warning restore 649
		
		public int Version
			=> 1;
		
		public ReturnCode Run()
		{
			string intermediateFolderPath = m_BuildParameters.GetOutputFilePathForIdentifier("");
			string buildFolderPath = Path.Combine(intermediateFolderPath, "..");
			string targetFolderPath = Path.Combine(buildFolderPath, m_ModificationParameters.TargetFolderName); 
			
			string intermediateAssembliesFolderPath = m_BuildParameters.GetOutputFilePathForIdentifier(BuilderConsts.OutputAssemblies);
			string intermediateBundlesFolderPath = m_BuildParameters.GetOutputFilePathForIdentifier(BuilderConsts.OutputBundles);
			string intermediateBlueprintsFolderPath = m_BuildParameters.GetOutputFilePathForIdentifier(BuilderConsts.OutputBlueprints);
			string intermediateLocalizationFolderPath = m_BuildParameters.GetOutputFilePathForIdentifier(BuilderConsts.OutputLocalization);
			
			string targetAssembliesFolderPath = Path.Combine(targetFolderPath, BuilderConsts.OutputAssemblies);
			string targetBundlesFolderPath = Path.Combine(targetFolderPath, BuilderConsts.OutputBundles);
			string targetBlueprintsFolderPath = Path.Combine(targetFolderPath, BuilderConsts.OutputBlueprints);
			string targetLocalizationFolderPath = Path.Combine(targetFolderPath, BuilderConsts.OutputLocalization);

			BuilderUtils.CopyFilesWithFoldersStructure(
				intermediateAssembliesFolderPath, targetAssembliesFolderPath, SearchOption.TopDirectoryOnly, i => i.EndsWith(".dll"));

			BuilderUtils.CopyFilesWithFoldersStructure(
				intermediateBundlesFolderPath, targetBundlesFolderPath);

			BuilderUtils.CopyFilesWithFoldersStructure(
				intermediateBlueprintsFolderPath, targetBlueprintsFolderPath);

			BuilderUtils.CopyFilesWithFoldersStructure(
				intermediateLocalizationFolderPath, targetLocalizationFolderPath);

			#region Deathwatch
			// Deathwatch: ship the two DW voice sound banks with the mod. Copy <mod source>\Audio\*.bnk straight
			// into the built mod's Audio\ folder so DeathwatchModMain.RegisterVoiceBankPath's direct (called from Initialize, no Harmony patch)
			// AkSoundEngine.AddBasePath(Modification.Path\Audio) resolves them. Copied from SOURCE (not an
			// intermediate output) because .bnk are inert binaries needing no build processing; the helper no-ops
			// if the Audio folder is absent, so this is safe when the banks aren't present.
			BuilderUtils.CopyFilesWithFoldersStructure(
				Path.Combine(m_ModificationParameters.SourcePath, "Audio"),
				Path.Combine(targetFolderPath, "Audio"),
				SearchOption.AllDirectories, i => i.EndsWith(".bnk"));
			#endregion

			File.Copy(
				Path.Combine(intermediateFolderPath, Kingmaker.Modding.OwlcatModification.ManifestFileName),
				Path.Combine(targetFolderPath, Kingmaker.Modding.OwlcatModification.ManifestFileName));
			
			File.Copy(
				Path.Combine(intermediateFolderPath, Kingmaker.Modding.OwlcatModification.SettingsFileName),
				Path.Combine(targetFolderPath, Kingmaker.Modding.OwlcatModification.SettingsFileName));

			return ReturnCode.Success;
		}
	}
}