using UnityEngine;

public class DebugTest : MonoBehaviour
{
    public string label = "Player";
    public float speed = 5.5f;
    private int counter = 0;

    void Update()
    {
        counter++;
        float delta = Time.deltaTime;

        ProcessFrame(counter, delta);
    }

    void ProcessFrame(int frame, float dt)
    {
        float moved = speed * dt;

        if (frame % 60 == 0)
        {
            Debug.Log($"[{label}] frame={frame}, moved={moved}");
        }
    }
}
