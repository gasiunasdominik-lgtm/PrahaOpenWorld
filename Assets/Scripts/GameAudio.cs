using System.Collections.Generic;
using UnityEngine;

// Headless audio floor: a static gain layer (Music / SFX) that every source multiplies through.
// Settings sliders bind to MusicGain / SfxGain. Upgrade path: an AudioMixer with exposed
// MusicVol / SFXVol parameters authored in the Editor's Audio Mixer window.
public class GameAudio : MonoBehaviour
{
    public static GameAudio I { get; private set; }

    static float musicGain = -1, sfxGain = -1;
    public static float MusicGain
    {
        get { if (musicGain < 0) musicGain = PlayerPrefs.GetFloat("MusicGain", 0.7f); return musicGain; }
        set { musicGain = Mathf.Clamp01(value); PlayerPrefs.SetFloat("MusicGain", musicGain); }
    }
    public static float SfxGain
    {
        get { if (sfxGain < 0) sfxGain = PlayerPrefs.GetFloat("SfxGain", 0.8f); return sfxGain; }
        set { sfxGain = Mathf.Clamp01(value); PlayerPrefs.SetFloat("SfxGain", sfxGain); }
    }

    public AudioClip blip, sting, click, footstep, door, crash, horn, engine;
    AudioSource music, ambience, ui;
    float duck = 1f;
    readonly List<(AudioSource src, float baseVol, bool isMusic)> managed = new();

    void Awake()
    {
        I = this;
        blip = AudioSynth.Blip(); sting = AudioSynth.Sting(); click = AudioSynth.Click();
        footstep = AudioSynth.Footstep(); door = AudioSynth.Door(); crash = AudioSynth.Crash();
        horn = AudioSynth.Horn(); engine = AudioSynth.Engine();

        music = MakeSource("Music", AudioSynth.MusicPad(), true, 0.55f, true);
        ambience = MakeSource("CityAmbience", AudioSynth.CityAmbience(), true, 0.35f, false);
        ui = MakeSource("UI", null, false, 1f, false);
        music.Play(); ambience.Play();
    }

    AudioSource MakeSource(string n, AudioClip clip, bool loop, float vol, bool isMusic)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.loop = loop; s.spatialBlend = 0; s.playOnAwake = false;
        Register(s, vol, isMusic);
        return s;
    }

    public void Register(AudioSource s, float baseVol, bool isMusic)
    {
        managed.Add((s, baseVol, isMusic));
        s.volume = baseVol * (isMusic ? MusicGain : SfxGain);
    }

    public void SetBaseVolume(AudioSource s, float baseVol)
    {
        for (int i = 0; i < managed.Count; i++)
            if (managed[i].src == s) { managed[i] = (s, baseVol, managed[i].isMusic); return; }
    }

    public void SetDuck(bool paused) => duck = paused ? 0.5f : 1f; // −6 dB while paused

    void Update()
    {
        for (int i = managed.Count - 1; i >= 0; i--)
        {
            var (src, vol, isMusic) = managed[i];
            if (src == null) { managed.RemoveAt(i); continue; }
            src.volume = vol * (isMusic ? MusicGain * duck : SfxGain * (isMusic ? 1 : duck));
        }
    }

    public void PlayUI(AudioClip c, float vol = 1f, float pitchVar = 0f)
    {
        if (c == null) return;
        ui.pitch = 1f + Random.Range(-pitchVar, pitchVar);
        ui.PlayOneShot(c, vol * SfxGain);
    }

    public void PlayAt(AudioClip c, Vector3 pos, float vol = 1f, float pitchVar = 0.08f)
    {
        if (c == null) return;
        var go = new GameObject("OneShot");
        go.transform.position = pos;
        var s = go.AddComponent<AudioSource>();
        s.clip = c; s.spatialBlend = 1f; s.minDistance = 4f; s.maxDistance = 80f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.pitch = 1f + Random.Range(-pitchVar, pitchVar);
        s.volume = vol * SfxGain;
        s.Play();
        Destroy(go, c.length / Mathf.Max(0.5f, s.pitch) + 0.1f);
    }
}
