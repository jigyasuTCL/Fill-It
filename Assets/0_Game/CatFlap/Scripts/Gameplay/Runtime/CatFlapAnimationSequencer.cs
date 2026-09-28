using System;

namespace CatFlap.Runtime
{
    public sealed class CatFlapAnimationSequencer
    {
        private int sequenceVersion;

        public bool IsBusy { get; private set; }

        public void Play(Action<Action> playAnimation, Action onComplete = null)
        {
            sequenceVersion++;
            int activeVersion = sequenceVersion;
            IsBusy = true;
            bool completed = false;

            void CompleteOnce()
            {
                if (completed || activeVersion != sequenceVersion)
                {
                    return;
                }

                completed = true;
                IsBusy = false;
                onComplete?.Invoke();
            }

            if (playAnimation == null)
            {
                CompleteOnce();
                return;
            }

            try
            {
                playAnimation(CompleteOnce);
            }
            catch
            {
                CompleteOnce();
                throw;
            }
        }

        public void Cancel()
        {
            sequenceVersion++;
            IsBusy = false;
        }
    }
}
