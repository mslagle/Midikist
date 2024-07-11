using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Midikist.Components
{
    public class Point
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public float Speed { get; set; }
        public double Time { get; set; }

        public override string ToString()
        {
            return $"{Time}:{Speed} - {Position}:{Rotation}";
        }
    }
}
