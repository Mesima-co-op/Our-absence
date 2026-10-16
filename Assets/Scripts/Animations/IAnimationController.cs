using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAnimationController
{
    public void ChangeAnimation(string animation, float crossfade, float duration, bool force = false);
}
