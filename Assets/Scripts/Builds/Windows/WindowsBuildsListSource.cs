using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

namespace Protobot.Builds.Windows {
    public class WindowsBuildsListSource : MonoBehaviour, IBuildsListSource {
        public event Action<List<BuildData>> OnGetData;

        public void GetData() {
            var saveFilePaths = Directory.EnumerateFiles(WindowsSavingConfig.saveDirectoryPath);

            var buildDatas = new List<BuildData>();

            if (saveFilePaths.Count() > 0) {
                foreach (string filePath in saveFilePaths.Where(filePath =>
                             filePath.Contains(WindowsSavingConfig.saveFileType) && !filePath.Contains(".meta"))) {
                    if (BuildSerialization.TryDeserializeBuild(filePath, out BuildData build) && build != null) {
                        buildDatas.Add(build);
                    }
                }
            }

            OnGetData?.Invoke(buildDatas);
        }
    }
}
