// #define LFE_DEBUG

using MVR.FileManagementSecure;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

#if LFE_DEBUG
using System.Diagnostics;
#endif

namespace LFE
{
    public class TextureAutoReload : MVRScript
    {
        private List<WatchedFile> _watchedFiles = new List<WatchedFile>();
        private float _pollingInterval = 1.0f;

        private bool _running = false;

        private MD5 _md5;

        public override void Init()
        {
            OnEnable();
        }

        // use on disable instead of ondestroy -- more predictable when merge loading and stuff
        public void OnDisable()
        {
            StopAllCoroutines();
            _running = false;

            foreach(var wf in _watchedFiles) {
                wf?.Dispose();
            }
            _watchedFiles = new List<WatchedFile>();

            _md5 = null;
        }

        public void OnEnable()
        {
            if (containingAtom.type != "Person")
            {
                SuperController.LogError("Must be placed on a person");
                _running = false;
                return;
            }

            _md5 = MD5.Create();

            var textureControl = containingAtom.GetComponentInChildren<DAZCharacterTextureControl>();
            if (textureControl != null)
            {
                foreach (var name in textureControl.GetUrlParamNames())
                {
                    var value = textureControl.GetUrlParamValue(name);

                    // skip - no custom texture set
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }
                    _watchedFiles.Add(new WatchedFile(this, value));
                }
            }

            _running = true;

            StartCoroutine(CheckForFileChangesCoroutine());
        }

        private void OnFileChanged(string fileName)
        {
#if LFE_DEBUG
            SuperController.LogMessage($"file changed: {fileName}");
#endif
            var textureControl = containingAtom.GetComponentInChildren<DAZCharacterTextureControl>();
            if (textureControl != null)
            {
                foreach (var name in textureControl.GetUrlParamNames())
                {
                    var value = textureControl.GetUrlParamValue(name);
                    if (fileName.Equals(value))
                    {
#if LFE_DEBUG
                        SuperController.LogMessage($"reload slot: {name}");
#endif

                        // setting the value using SetUrlParamValue does not trigger reload
                        // if the values are the same use SetFilePath instead
                        var storable = textureControl.GetUrlJSONParam(name);
                        if (storable != null)
                        {
                            try
                            {
                                storable.SetFilePath(fileName);
                            }
                            catch (Exception e)
                            {
                                if (e.ToString().Contains("Sharing violation on path"))
                                {
                                    // external program is writing to the file, just wait until next poll
                                }
                                else
                                {
                                    SuperController.LogError(e.ToString());
                                }
                            }
                        }
                    }
                }
            }
        }

        private IEnumerator CheckForFileChangesCoroutine()
        {
            while (_running)
            {
                foreach (var wf in _watchedFiles)
                {
                    if(wf.HasChanged()) {
                        OnFileChanged(wf.Path);
                    }
                }

                yield return new WaitForSeconds(Mathf.Abs(_pollingInterval));
            }
        }

        public string FileFingerPrint(string fileName)
        {
            fileName = FileManagerSecure.NormalizePath(fileName);
#if LFE_DEBUG
            var sw = new Stopwatch();
            sw.Start();
#endif
            var inBytes = FileManagerSecure.ReadAllBytes(fileName);
#if LFE_DEBUG
            sw.Stop();
            SuperController.LogMessage($"fingerprint: read {fileName} took: {sw.ElapsedMilliseconds}ms");
            sw.Reset();
            sw.Start();
#endif
            var outBytes = _md5.ComputeHash(inBytes);
            var sb = new StringBuilder();
            for (var i = 0; i < outBytes.Length; i++)
            {
                sb.Append(outBytes[i].ToString("X2"));
            }
            var print = sb.ToString();
#if LFE_DEBUG
            sw.Stop();
            SuperController.LogMessage($"fingerprint: hash {fileName} took: {sw.ElapsedMilliseconds}ms");
#endif
            return print;
        }
    }

    public class WatchedFile : IDisposable
    {
        public string Path { get; private set; }

        public bool Enabled => _storable.val;
        public float CheckSeconds => 1.0f;

        private TextureAutoReload _plugin;
        private JSONStorableBool _storable;
        private UIDynamicToggle _ui;
        private string _lastHash;
        private DateTime _lastChecked;
        private const int MAX_PATH_LABEL_LENGTH = 30;

        public WatchedFile(TextureAutoReload plugin, string path)
        {
            var truncatedPath = path.Length < MAX_PATH_LABEL_LENGTH ? path : "..." + path.Substring(path.Length - MAX_PATH_LABEL_LENGTH);

            Path = path;
            _plugin = plugin;
            _storable = new JSONStorableBool(path, false);
            _ui = _plugin.CreateToggle(_storable);
            _ui.labelText.resizeTextForBestFit = true;
            _ui.labelText.text = truncatedPath;
            _lastChecked = DateTime.Now;
            _lastHash = String.Empty;
        }

        public bool HasChanged() {
            if(!Enabled) {
                return false;
            }

            if(_lastChecked.AddSeconds(CheckSeconds) > DateTime.Now) {
                return false;
            }

            try
            {
                _lastChecked = DateTime.Now;
                var newHash = _plugin.FileFingerPrint(Path);
                // var newHash = Path;
                if(_lastHash.Equals(String.Empty)) {
                    _lastHash = newHash;
                    return false;
                }
                if (!newHash.Equals(_lastHash))
                {
                    _lastHash = newHash;
                    return true;
                }
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("Sharing violation on path"))
                {
                    // external program is writing to the file, just wait until next poll
                    return false;
                }
                else
                {
                    SuperController.LogError(e.ToString());
                }
            }
            return false;
        }

        public void Dispose() {
            _plugin?.RemoveToggle(_ui);
        }
    }
}
