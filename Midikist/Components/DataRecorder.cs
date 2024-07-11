using BepInEx.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using ZeepSDK.Level;
using ZeepSDK.LevelEditor;
using ZeepSDK.Storage;

namespace Midikist.Components
{
    public class DataRecorder : MonoBehaviour
    {
        private bool isRunning;
        private Coroutine recordRoutine;
        private readonly List<Point> data = new List<Point>();
        private readonly DateTime start = DateTime.Now;

        private readonly ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource("DataRecorder");

        private void Awake()
        {
            this.isRunning = true;
            this.recordRoutine = this.StartCoroutine(this.RecordEnumerator());
            this.logger.LogInfo("DataRecorder is awake");

        }

        private void OnDestroy()
        {
            this.StopCoroutine(this.recordRoutine);
            this.isRunning = false;

            if (LevelApi.CurrentLevel != null)
            {
                this.logger.LogInfo("Saving recorded data now.");
                Plugin.Storage.SaveToJson(LevelApi.CurrentLevel.UID, this.data);

                PlayerManager.Instance.messenger.Log($"Created a point recording {this.data.Max(x => x.Time) / 1000} seconds long", 1f);
            }

            this.logger.LogInfo("DataRecorder is destroyed");
        }

        private IEnumerator RecordEnumerator()
        {
            //int lod = Mathf.Clamp(1, 1, 10);
            float delay = .1f;
            Transform t = this.transform;

            while (this.isRunning)
            {
                data.Add(new Point()
                {
                    Position = t.position + t.up,
                    Rotation = t.rotation,
                    Time = (DateTime.Now - start).TotalMilliseconds,
                    Speed = data.Count > 0 ? Vector3.Distance(t.position + t.up, data.Last().Position) / delay : 0
                });
                yield return (object)new WaitForSeconds(delay);
            }
        }
    }
}
