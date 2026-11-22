using UnityEngine;

namespace ProjectArena
{
    // Development builds only: logs the head camera's pose once a second (adb logcat -s Unity), so a headset screenshot
    // can be matched by an editor render from the same spot (Editor/SceneShots.cs RunPose).
    public class DebugPoseLog : MonoBehaviour
    {
        float next;

        void Update()
        {
            if (!Debug.isDebugBuild || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            var t = transform;
            var p = t.position;
            var q = t.rotation;
            var cam = GetComponent<Camera>();
            Debug.Log($"[Pose] {p.x:F3} {p.y:F3} {p.z:F3} {q.x:F4} {q.y:F4} {q.z:F4} {q.w:F4} fov {(cam ? cam.fieldOfView : 0f):F1}");
        }
    }
}
