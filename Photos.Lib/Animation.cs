using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class Animation
    {
        public Animation(BaseView view)
        {
            _view = view;
            Stopped = true;
        }

        public Animation(BaseView view, Action act)
        {
            Animate = act;
            _view = view;
            Stopped = true;
        }

        public void Start()
        {
            _view.Window.EnsureWindowThread();

            lastTime = DateTime.UtcNow;

            if (Stopped)
            {
                _view.Window._animations.Add(this);
                _view.Invalidate();
            }
            Stopped = false;

        }

        BaseView _view;

        public float TimeDeltaSec => Math.Min((float)(DateTime.UtcNow - lastTime).TotalSeconds, 0.05f);
        public Action Animate;
        internal DateTime lastTime;
        public bool Stopped { get; internal set; }

        public void Stop()
        {
            _view.Window.EnsureWindowThread();

            if (!Stopped)
                _view.Window._animations.Remove(this);
            Stopped = true;
        }

        internal virtual void Run()
        {
            Animate();
            lastTime = DateTime.UtcNow;
        }
    }

    public class SpringAnimation : Animation
    {
        public float X, V, Mass, Damping, Stiffness;
        public SpringAnimation(BaseView v) : base(v)
        {
        }

        internal override void Run()
        {
            //Debug.WriteLine($"x {X}, v {V}");
            //var realDt = TimeDeltaSec; // does not work with real time (different speed)
            var dt = 0.02f;

            var a = (-Stiffness) * X / Mass;
            V += a * dt;
            if (Math.Sign(X) == Math.Sign(V) || Math.Abs(X) < 3)
                V *= (Damping * Damping * Damping * Damping);
            else
                V *= Damping;
            X += V * dt;

            if (Math.Abs(V) < 30 && Math.Abs(X) < 3)
            {
                V = X = 0;
                Stop();
            }

            base.Run();
        }
    }
}
