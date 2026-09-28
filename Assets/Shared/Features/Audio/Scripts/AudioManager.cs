using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using Shared.PlayerData;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;

    public static AudioManager Instance
    {
        get
        {
            if(_instance) return _instance;
            
            _instance = FindFirstObjectByType<AudioManager>();
            if (!_instance)
                _instance = Instantiate(Resources.Load<AudioManager>(nameof(AudioManager)));
            
            _instance.InitService();
            return _instance;
        }
    }

    public UnityAction<string> PlaySoundAction = delegate { };
    public UnityAction<bool> PauseMusicAction = delegate { };
    public UnityAction AudionSettingAction = delegate { };

    private readonly List<AudioSceneData> _soundSceneDataList = new ();
    
    public AudioSource soundSource;
    public AudioSource musicSource;
    
    public AudioMixer audioMixer;

    private float _targetMusicVolume;
    private Coroutine _musicFadeRoutine;
    
    private bool _isSoundServicesInitialized;
    
    private const string MusicParam = "MusicVolume";
    private const string SfxParam = "SFXVolume";
    
    private void Awake()
    {
        if (_instance)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        InitService();
    }

    private void InitService()
    {
        if (_isSoundServicesInitialized) return;
        _isSoundServicesInitialized = true;
        
        if (musicSource != null)
        {
            musicSource.loop = true;
        }

        PlaySoundAction = null;
        PlaySoundAction += PlayAudio;
        
        PauseMusicAction = null;
        PauseMusicAction += PauseMusic;
        
        AudionSettingAction = null;
        AudionSettingAction += UpdateAudioSettings;
        
        _targetMusicVolume = musicSource != null ? musicSource.volume : 1f;

        UpdateAudioSettings();
    }
    
    private void UpdateAudioSettings()
    {
        if (audioMixer == null)
        {
            return;
        }

        audioMixer.SetFloat(MusicParam, PlayerData.Instance.IsMusicOn ? 0f : -80f);
        audioMixer.SetFloat(SfxParam, PlayerData.Instance.IsSoundOn ? 0f : -80f);
    }

    private void PlayAudio(string clipName)
    {
        var soundData = new SoundData();
        
        var isMusicSoundData = false;
        foreach (var soundSceneData in _soundSceneDataList)
        {
            if (soundSceneData)
            {
                soundData = soundSceneData.GetSoundData(clipName);
                if (soundData.clip)
                    break;

                soundData = soundSceneData.GetMusicData(clipName);
                if (soundData.clip)
                {
                    isMusicSoundData = true;
                    break;
                }
            }
        }
        
        if (!soundData.clip || !soundData.isActive)
        {
            return;
        }

        if (isMusicSoundData)
        {
            if (musicSource == null)
            {
                return;
            }

            musicSource.clip = soundData.clip;
            if (!PlayerData.Instance.IsMusicOn)
            {
                return;
            }

            PlayBgMusic();
            return;
        }

        if (!PlayerData.Instance.IsSoundOn)
        {
            return;
        }

        if (soundSource != null)
        {
            soundSource.PlayOneShot(soundData.clip);
        }
    }

    private void PauseMusic(bool isPause)
    {
        if (musicSource == null)
        {
            return;
        }

        if(isPause)
            musicSource.Pause();
        else if(musicSource.clip)
            PlayBgMusic();
    }

    void OnDestroy()
    {
        PlaySoundAction = null;
    }
    
    void PlayBgMusic()
    {
        if (musicSource == null)
        {
            return;
        }

        if (_musicFadeRoutine != null)
        {
            StopCoroutine(_musicFadeRoutine);
            _musicFadeRoutine = null;
        }

        musicSource.volume = 0f;
        musicSource.Play();
        _musicFadeRoutine = StartCoroutine(FadeMusicVolume(_targetMusicVolume, 1f));
    }

    private IEnumerator FadeMusicVolume(float targetVolume, float duration)
    {
        float startVolume = musicSource.volume;
        duration = Mathf.Max(0.01f, duration);

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 0.5f - Mathf.Cos(t * Mathf.PI) * 0.5f;
            musicSource.volume = Mathf.Lerp(startVolume, targetVolume, eased);
            yield return null;
        }

        musicSource.volume = targetVolume;
        _musicFadeRoutine = null;
    }
    
    public void AddSoundSceneData(AudioSceneData audioSceneData)
    {
        _soundSceneDataList.RemoveAll(x => !x);
        
        if (!_soundSceneDataList.Contains(audioSceneData))
            _soundSceneDataList.Add(audioSceneData);
    }
    
    public void RemoveSoundSceneData(AudioSceneData audioSceneData)
    {
        _soundSceneDataList.RemoveAll(x => !x);
        
        if (_soundSceneDataList.Contains(audioSceneData))
            _soundSceneDataList.Remove(audioSceneData);
    }

    public static bool IsInstanceExist()
    {
        return _instance;
    }

    public static void PlayClickSound()
    {
        Instance.PlaySoundAction.Invoke(SoundName.ClickSound);
    }
}

[Serializable]
public struct SoundData
{
    public string name;
    
    public AudioClip clip;

    public bool isActive;
}

public static class SoundName
{
    public const string LevelStart = "LevelStart";
    public const string LevelComplete = "LevelComplete";
    public const string LevelFail = "LevelFail";
    
    public const string HomeScreenBg = "homebg";
    public const string GamePlayBg = "GamePlayBg";
    public const string HardLevelBg = "HardLevelBg";
    public const string ClickSound = "ClickSound";
    public const string PopupOpen = "PopupOpen";
    public const string CellTap = "CellTap";
    public const string CellWrongTap = "CellWrongTap";
    public const string CellCorrectMatch = "CellCorrectMatch";
    public const string CoinCollect = "CoinCollect";


    /// =--- new Added --------------//
    public const string TapFish = "TapFish";
    public const string TapCat = "TapCat";
    public const string TapEmpty = "TapEmpty";
    public const string LineComplete = "LineComplete";
    public const string LineWrong = "LineWrong";
    public const string HintUsed = "HintUsed";
    public const string NotesUsed = "NotesUsed";
    public const string UndoUsed = "UndoUsed";

    /// =--- Cat_Flap_Sound_Pack v3 --------------//
    public const string BoardTileAppear = "BoardTileAppear";
    public const string BoardTilesIntro = "BoardTilesIntro";
    public const string CellLockedTap = "CellLockedTap";
    public const string MoveErrorSoft = "MoveErrorSoft";
    public const string RuleBreakError = "RuleBreakError";
    public const string LifeEmpty = "LifeEmpty";
    public const string HeartLose = "HeartLose";
    public const string LevelCoinReward = "LevelCoinReward";
    public const string LevelStarTally = "LevelStarTally";
    public const string LineCompleteDouble = "LineCompleteDouble";
    public const string LineProgressHalf = "LineProgressHalf";
    public const string ButtonBack = "ButtonBack";
    public const string ActionBlocked = "ActionBlocked";
    public const string MenuMove = "MenuMove";
    public const string ModalClose = "ModalClose";
    public const string SolveStep = "SolveStep";
    public const string ToggleOff = "ToggleOff";
    public const string ToggleOn = "ToggleOn";

    public const string SpinTheWheel = "SpinTheWheel";

    public const string LwinConfitti = "LwinConfitti";

    public const string RewardScreen = "RewardScreen";
    public const string CollectReward = "CollectReward";

    public const string HardLevelPopupOpen = "HardLevelPopupOpen";

}
