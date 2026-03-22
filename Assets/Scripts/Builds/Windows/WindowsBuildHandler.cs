using UnityEngine;
using System;
using System.Collections;
using System.IO;

namespace Protobot.Builds.Windows {
    public class WindowsBuildHandler : MonoBehaviour, IBuildHandler {
        public void Save(BuildData buildData) {
            string fileLocation = GetFileLocation(buildData);
            BuildSerialization.TrySerializeBuild(fileLocation, buildData);
        }

        public void Delete(BuildData buildData) {
            var fileLocation = GetFileLocation(buildData);
            File.Delete(fileLocation);
        }

        public string GetFileLocation(BuildData buildData) {
            return WindowsSavingConfig.saveDirectoryPath + "/" + buildData.fileName + WindowsSavingConfig.saveFileType;
        }

        public DateTime GetExactWriteTime(BuildData buildData) {
            var fileLocation = GetFileLocation(buildData);
            return File.GetLastWriteTime(fileLocation);
        }
    }
}
