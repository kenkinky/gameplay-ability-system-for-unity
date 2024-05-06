using System.Collections.Generic;
using GAS.General;
using UnityEngine;
using UnityEngine.Profiling;

namespace GAS.Runtime
{
    internal abstract class RuntimeClipInfo
    {
        public int endFrame;
        public int startFrame;
    }

    internal class RuntimeDurationCueClip : RuntimeClipInfo
    {
        public GameplayCueDurationalSpec cueSpec;
    }

    internal class RuntimeBuffClip : RuntimeClipInfo
    {
        public GameplayEffect buff;
        public GameplayEffectSpec buffSpec;
    }

    internal class RuntimeTaskClip : RuntimeClipInfo
    {
        public OngoingAbilityTask task;
    }

    internal class RuntimeTaskMark
    {
        public int startFrame;
        public InstantAbilityTask task;
    }

    public class TimelineAbilityPlayer<T> where T : AbstractAbility
    {
        private readonly TimelineAbilitySpecT<T> _abilitySpec;
        private readonly List<RuntimeBuffClip> _cacheBuffGameplayEffectTrack = new();

        private readonly List<RuntimeDurationCueClip> _cacheDurationalCueTrack = new();


        private readonly List<InstantCueMarkEvent> _cacheInstantCues = new();

        private readonly List<RuntimeTaskMark> _cacheInstantTasks = new();
        private readonly List<RuntimeTaskClip> _cacheOngoingTaskTrack = new();

        private readonly List<ReleaseGameplayEffectMarkEvent> _cacheReleaseGameplayEffect = new();

        // cache for target catcher, avoid new in TickFrame
        // 这个是一个泛型类, 这个变量就不作为static了
        private readonly List<AbilitySystemComponent> _targets = new();

        private int _currentFrame;
        private float _playTotalTime;

        public TimelineAbilityPlayer(TimelineAbilitySpecT<T> abilitySpec)
        {
            _abilitySpec = abilitySpec;
            Cache();
        }

        public bool IsPlaying { get; private set; }

        public TimelineAbilityAsset AbilityAsset => _abilitySpec.Ability.DataReference as TimelineAbilityAsset;
        private int FrameCount => AbilityAsset.FrameCount;
        private int FrameRate => GASTimer.FrameRate;

        private void Cache()
        {
            _cacheInstantCues.Clear();
            foreach (var trackData in AbilityAsset.InstantCues)
                _cacheInstantCues.AddRange(trackData.markEvents);
            _cacheInstantCues.Sort((a, b) => a.startFrame.CompareTo(b.startFrame));

            _cacheReleaseGameplayEffect.Clear();
            foreach (var trackData in AbilityAsset.ReleaseGameplayEffect)
                _cacheReleaseGameplayEffect.AddRange(trackData.markEvents);
            _cacheReleaseGameplayEffect.Sort((a, b) => a.startFrame.CompareTo(b.startFrame));
            for (var i = 0; i < _cacheReleaseGameplayEffect.Count; i++)
                _cacheReleaseGameplayEffect[i].CacheTargetCatcher();

            _cacheInstantTasks.Clear();
            foreach (var trackData in AbilityAsset.InstantTasks)
            foreach (var markEvent in trackData.markEvents)
            foreach (var taskData in markEvent.InstantTasks)
            {
                var runtimeTaskMark = new RuntimeTaskMark
                {
                    startFrame = markEvent.startFrame,
                    task = taskData.CreateTask(_abilitySpec)
                };
                _cacheInstantTasks.Add(runtimeTaskMark);
            }

            _cacheInstantTasks.Sort((a, b) => a.startFrame.CompareTo(b.startFrame));

            _cacheDurationalCueTrack.Clear();
            foreach (var track in AbilityAsset.DurationalCues)
            foreach (var clipEvent in track.clipEvents)
            {
                var cueSpec = clipEvent.cue.ApplyFrom(_abilitySpec);
                if (cueSpec == null) continue;
                var runtimeDurationCueClip = new RuntimeDurationCueClip
                {
                    startFrame = clipEvent.startFrame,
                    endFrame = clipEvent.EndFrame,
                    cueSpec = cueSpec
                };
                _cacheDurationalCueTrack.Add(runtimeDurationCueClip);
            }

            _cacheBuffGameplayEffectTrack.Clear();
            foreach (var track in AbilityAsset.BuffGameplayEffects)
            foreach (var clipEvent in track.clipEvents)
                // 只有持续型的GameplayEffect可视作buff
                if (clipEvent.gameplayEffect.DurationPolicy is EffectsDurationPolicy.Duration
                    or EffectsDurationPolicy.Infinite)
                {
                    var runtimeBuffClip = new RuntimeBuffClip
                    {
                        startFrame = clipEvent.startFrame,
                        endFrame = clipEvent.EndFrame,
                        buff = new GameplayEffect(clipEvent.gameplayEffect),
                        buffSpec = null
                    };
                    _cacheBuffGameplayEffectTrack.Add(runtimeBuffClip);
                }

            _cacheOngoingTaskTrack.Clear();
            foreach (var track in AbilityAsset.OngoingTasks)
            foreach (var clip in track.clipEvents)
            {
                var runtimeTaskClip = new RuntimeTaskClip
                {
                    startFrame = clip.startFrame,
                    endFrame = clip.EndFrame,
                    task = clip.ongoingTask.CreateTask(_abilitySpec)
                };
                _cacheOngoingTaskTrack.Add(runtimeTaskClip);
            }
        }

        private void Prepare()
        {
            for (var i = 0; i < _cacheBuffGameplayEffectTrack.Count; i++)
                _cacheBuffGameplayEffectTrack[i].buffSpec = null;
        }

        public void Play()
        {
            _currentFrame = -1; // 为了播放第0帧
            _playTotalTime = 0;
            IsPlaying = true;
            Prepare();
        }

        public void Stop()
        {
            if (!IsPlaying) return;

            foreach (var clip in _cacheDurationalCueTrack) clip.cueSpec.OnRemove();

            foreach (var clip in _cacheBuffGameplayEffectTrack)
                if (clip.buffSpec != null)
                    _abilitySpec.Owner.RemoveGameplayEffect(clip.buffSpec);

            foreach (var clip in _cacheOngoingTaskTrack) clip.task.OnEnd(clip.endFrame);

            IsPlaying = false;
        }

        public void Tick()
        {
            if (!IsPlaying) return;
            Profiler.BeginSample($"{nameof(TimelineAbilityPlayer<T>)}::Tick()  ability name: {_abilitySpec.Ability.Name}");

            _playTotalTime += Time.deltaTime;
            var targetFrame = (int)(_playTotalTime * FrameRate);

            // 追帧
            while (_currentFrame < targetFrame)
            {
                _currentFrame++;
                TickFrame(_currentFrame);
            }

            if (_currentFrame >= FrameCount)
            {
                OnPlayEnd();
            }

            Profiler.EndSample();
        }

        /// <summary>
        ///     播放结束
        /// </summary>
        private void OnPlayEnd()
        {
            Profiler.BeginSample("OnPlayEnd()");
            IsPlaying = false;

            if (!AbilityAsset.manualEndAbility)
                _abilitySpec.TryEndAbility();
            Profiler.EndSample();
        }

        /// <summary>
        ///     当前帧的事件
        /// </summary>
        /// <param name="frame"></param>
        private void TickFrame(int frame)
        {
            Profiler.BeginSample($"{nameof(TimelineAbilityPlayer<T>)}::TickFrame()");

            // Cue 即时
            Profiler.BeginSample($"instantCues");
            foreach (var cueMark in _cacheInstantCues)
            {
                if (frame == cueMark.startFrame)
                {
                    foreach (var cue in cueMark.cues)
                    {
                        Profiler.BeginSample("cue.ApplyFrom()");
                        cue.ApplyFrom(_abilitySpec);
                        Profiler.EndSample();
                    }
                }
            }

            Profiler.EndSample();


            // 释放型GameplayEffect
            Profiler.BeginSample($"releaseGameplayEffect");
            foreach (var mark in _cacheReleaseGameplayEffect)
            {
                if (frame == mark.startFrame)
                {
                    var catcher = mark.TargetCatcher;
                    catcher.Init(_abilitySpec.Owner);

                    Profiler.BeginSample("catcher.CatchTargets()");
                    catcher.CatchTargetsNonAllocSafe(_abilitySpec.Target, _targets);
                    Profiler.EndSample();

                    foreach (var asc in _targets)
                    {
                        foreach (var effect in mark.gameplayEffectAssets)
                        {
                            Profiler.BeginSample("[GC Mark] new GameplayEffect()");
                            var ge = new GameplayEffect(effect);
                            Profiler.EndSample();

                            Profiler.BeginSample("releaseGameplayEffect.ApplyGameplayEffectTo()");
                            _abilitySpec.Owner.ApplyGameplayEffectTo(ge, asc);
                            Profiler.EndSample();
                        }
                    }
                    
                    _targets.Clear();
                }
            }

            Profiler.EndSample();


            // Instant Task
            Profiler.BeginSample($"instantTasks");
            foreach (var instantTask in _cacheInstantTasks)
            {
                if (frame == instantTask.startFrame)
                {
                    Profiler.BeginSample("instantTask.OnExecute()");
                    instantTask.task.OnExecute();
                    Profiler.EndSample();
                }
            }

            Profiler.EndSample();


            // Cue 持续
            Profiler.BeginSample("durationalCues");
            foreach (var cueClip in _cacheDurationalCueTrack)
            {
                if (frame == cueClip.startFrame)
                {
                    Profiler.BeginSample("durationalCues.OnAdd()");
                    cueClip.cueSpec.OnAdd();
                    Profiler.EndSample();
                }

                if (frame >= cueClip.startFrame && frame <= cueClip.endFrame)
                {
                    Profiler.BeginSample("durationalCues.OnTick()");
                    cueClip.cueSpec.OnTick();
                    Profiler.EndSample();
                }

                if (frame == cueClip.endFrame)
                {
                    Profiler.BeginSample("durationalCues.OnRemove()");
                    cueClip.cueSpec.OnRemove();
                    Profiler.EndSample();
                }
            }

            Profiler.EndSample();

            // Buff型GameplayEffect
            // buff持续时间以Timeline配置时间为准（执行策略全部改为Infinite）
            Profiler.BeginSample("buffGameplayEffect");
            foreach (var buffClip in _cacheBuffGameplayEffectTrack)
            {
                if (frame == buffClip.startFrame)
                {
                    Profiler.BeginSample("buffGameplayEffect.Start");
                    var buffSpec = _abilitySpec.Owner.ApplyGameplayEffectToSelf(buffClip.buff);
                    buffSpec.SetDurationPolicy(EffectsDurationPolicy.Infinite);
                    buffClip.buffSpec = buffSpec;
                    Profiler.EndSample();
                }

                if (frame == buffClip.endFrame)
                {
                    if (buffClip.buffSpec != null)
                    {
                        Profiler.BeginSample("buffGameplayEffect.End");
                        _abilitySpec.Owner.RemoveGameplayEffect(buffClip.buffSpec);
                        Profiler.EndSample();
                    }

                    buffClip.buffSpec = null;
                }
            }

            Profiler.EndSample();

            // Ongoing Task
            Profiler.BeginSample("ongoingTasks");
            foreach (var taskClip in _cacheOngoingTaskTrack)
            {
                if (frame == taskClip.startFrame)
                {
                    Profiler.BeginSample("Ongoing Task.OnStart()");
                    taskClip.task.OnStart(frame);
                    Profiler.EndSample();
                }

                if (frame >= taskClip.startFrame && frame <= taskClip.endFrame)
                {
                    Profiler.BeginSample("Ongoing Task.OnTick()");
                    taskClip.task.OnTick(frame, taskClip.startFrame, taskClip.endFrame);
                    Profiler.EndSample();
                }

                if (frame == taskClip.endFrame)
                {
                    Profiler.BeginSample("Ongoing Task.OnEnd()");
                    taskClip.task.OnEnd(frame);
                    Profiler.EndSample();
                }
            }

            Profiler.EndSample();

            Profiler.EndSample();
        }
    }
}