﻿using OsuPlayground.OsuMath;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OsuPlayground.HitObjects
{
    // This code was adapted from the following file:
    // https://github.com/ppy/osu/blob/master/osu.Game/Rulesets/Objects/SliderCurve.cs
    public class Slider : HitObject
    {
        public float Length;

        public List<Vector2> ControlPoints;

        public CurveType CurveType = CurveType.PerfectCurve;

        private readonly List<Vector2> calculatedPath = new List<Vector2>();
        private readonly List<float> cumulativeLength = new List<float>();

        private List<Vector2> CalculateSubpath(List<Vector2> subControlPoints)
        {
            switch (this.CurveType)
            {
                case CurveType.Linear:
                    return subControlPoints;
                case CurveType.PerfectCurve:
                    if (this.ControlPoints.Count != 3 || subControlPoints.Count != 3)
                    {
                        break;
                    }

                    List<Vector2> subpath = new CircularArcApproximator(subControlPoints[0], subControlPoints[1], subControlPoints[2]).CreateArc();

                    if (subpath.Count == 0)
                    {
                        break;
                    }

                    return subpath;
                case CurveType.Catmull:
                    return new CatmullApproximator(subControlPoints).CreateCatmull();
            }

            return new BezierApproximator(subControlPoints).CreateBezier();
        }

        private void CalculatePath()
        {
            this.calculatedPath.Clear();

            List<Vector2> subControlPoints = new List<Vector2>();
            for (int i = 0; i < this.ControlPoints.Count; ++i)
            {
                subControlPoints.Add(this.ControlPoints[i]);
                if (i == this.ControlPoints.Count - 1 || this.ControlPoints[i] == this.ControlPoints[i + 1])
                {
                    List<Vector2> subpath = CalculateSubpath(subControlPoints);
                    foreach (Vector2 t in subpath)
                    {
                        if (this.calculatedPath.Count == 0 || this.calculatedPath.Last() != t)
                        {
                            this.calculatedPath.Add(t);
                        }
                    }

                    subControlPoints.Clear();
                }
            }
        }

        private void CalculateCumulativeLengthAndTrimPath()
        {
            // If the length has already been defined by the user, don't recalculate it.
            if (this.Length == 0)
            {
                var lengthPerBeat = 100 * Options.SliderMultiplier.Value * Options.SpeedMultiplier.Value;
                var lengthPerSnap = lengthPerBeat / Options.BeatSnapDivisor.Value;

                float length = 0;

                for (int i = 0; i < this.calculatedPath.Count - 1; i++)
                {
                    length += (this.calculatedPath[i + 1] - this.calculatedPath[i]).magnitude;
                }

                this.Length = length - (length % lengthPerSnap);
            }

            float l = 0;

            this.cumulativeLength.Clear();
            this.cumulativeLength.Add(l);

            for (int i = 0; i < this.calculatedPath.Count - 1; ++i)
            {
                l += (this.calculatedPath[i + 1] - this.calculatedPath[i]).magnitude;
                this.cumulativeLength.Add(l);
            }

            if (l < this.Length && this.calculatedPath.Count > 1)
            {
                Vector2 diff = this.calculatedPath[this.calculatedPath.Count - 1] - this.calculatedPath[this.calculatedPath.Count - 2];
                float d = diff.magnitude;

                if (d <= 0)
                {
                    return;
                }

                this.calculatedPath[this.calculatedPath.Count - 1] += diff * ((this.Length - l) / d);
                this.cumulativeLength[this.calculatedPath.Count - 1] = this.Length;
            }
        }

        private int IndexOfLength(float d)
        {
            int i = this.cumulativeLength.BinarySearch(d);
            if (i < 0)
            {
                i = ~i;
            }

            return i;
        }

        private float ProgressToLength(float progress) => Math.Max(Math.Min(progress, 1), 0) * this.Length;

        private Vector2 InterpolateVertices(int i, float d)
        {
            if (this.calculatedPath.Count == 0)
            {
                return Vector2.zero;
            }

            if (i <= 0)
            {
                return this.calculatedPath.First();
            }
            else if (i >= this.calculatedPath.Count)
            {
                return this.calculatedPath.Last();
            }

            Vector2 p0 = this.calculatedPath[i - 1];
            Vector2 p1 = this.calculatedPath[i];

            float d0 = this.cumulativeLength[i - 1];
            float d1 = this.cumulativeLength[i];

            if (Precision.AlmostEquals(d0, d1))
            {
                return p0;
            }

            float w = (d - d0) / (d1 - d0);
            return p0 + (p1 - p0) * w;
        }

        public List<Vector2> GetPath()
        {
            CalculatePath();
            CalculateCumulativeLengthAndTrimPath();

            float d0 = ProgressToLength(0);
            float d1 = ProgressToLength(1);

            var path = new List<Vector2>();

            int i = 0;
            for (; i < this.calculatedPath.Count && this.cumulativeLength[i] < d0; ++i)
            { }

            path.Add(InterpolateVertices(i, d0));

            for (; i < this.calculatedPath.Count && this.cumulativeLength[i] <= d1; ++i)
            {
                path.Add(this.calculatedPath[i]);
            }

            path.Add(InterpolateVertices(i, d1));

            for (int j = 0; path.Count > 2 && j < path.Count - 1; j++)
            {
                if (path[j] == path[j + 1])
                {
                    path.RemoveAt(j + 1);
                    j--;
                }
            }

            return path;
        }

        public Vector2 PositionAt(float progress)
        {
            CalculatePath();
            CalculateCumulativeLengthAndTrimPath();

            float d = ProgressToLength(progress);
            return InterpolateVertices(IndexOfLength(d), d);
        }

        public override string ToString()
        {
            string type = String.Empty;

            switch (this.CurveType)
            {
                case CurveType.Catmull:
                    type = "C";
                    break;
                case CurveType.Bezier:
                    type = "B";
                    break;
                case CurveType.Linear:
                    type = "L";
                    break;
                case CurveType.PerfectCurve:
                    type = "P";
                    break;
            }

            return String.Format("slider: {0},{1} [...] {2}|{3},1,{4}",
                Math.Round(this.Position.x),
                Math.Round(this.Position.y),
                type,
                String.Join("|", this.ControlPoints.Skip(1).Select(x => String.Format("{0}:{1}", Math.Round(x.x), Math.Round(x.y))).ToArray()),
                this.Length);
        }

    }
}
