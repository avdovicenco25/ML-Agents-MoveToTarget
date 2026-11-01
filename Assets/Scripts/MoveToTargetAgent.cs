using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.Splines;
using float3 = Unity.Mathematics.float3; // alias avoids the Random.Range ambiguity

public class MoveToTargetAgent : Agent
{
    [Header("References")]
    [SerializeField] private SplineContainer road;   // the spline GameObject (NOT a Spline field)
    [SerializeField, FormerlySerializedAs("floor")] private Renderer agentMesh; // optional: the cube's Mesh Renderer, flashes green/red

    [Header("Car")]
    [SerializeField] private float maxSpeed = 12f;
    [SerializeField] private float acceleration = 15f;
    [SerializeField] private float turnSpeed = 120f; // degrees per second
    [SerializeField] private float spawnHeight = 0.5f;

    [Header("Road")]
    [SerializeField] private float halfRoadWidth = 4f;   // distance from spline to wall
    [SerializeField] private float lookAheadNear = 5f;   // metres
    [SerializeField] private float lookAheadFar = 15f;   // metres
    [SerializeField, Range(0f, 1f)] private float randomStartChance = 0.7f;

    [Header("Rewards")]
    [SerializeField] private float progressReward = 1f;     // total reward for driving the whole track
    [SerializeField] private float finishBonus = 1f;
    [SerializeField] private float wallHitPenalty = 0.05f;  // once, when first touching a wall
    [SerializeField] private float wallStayPenalty = 0.005f; // every step while scraping a wall
    [SerializeField] private float stuckSeconds = 8f;       // no new progress for this long = episode ends

    [Header("Colors")]
    [SerializeField] private Color winColour = Color.green;
    [SerializeField] private Color loseColour = Color.red;
    [SerializeField] private Color idleColour = Color.grey;

    private Rigidbody rb;
    private float trackLength;

    // road state, refreshed in CollectObservations
    private float curT, lastT, bestT;
    private float lateral;        // -1..1 = left/right edge of the road, 0 = centre
    private Vector3 tangent;      // road direction at the nearest point (flat)

    private float lastProgressTime;
    private bool touchingWall;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        trackLength = road.CalculateLength(0);
        SetColour(idleColour);
    }

    public override void OnEpisodeBegin()
    {
        // Start at the beginning most of the time, otherwise somewhere along the track
        // (so the agent also practises the later corners instead of only the first one).
        float startT = Random.value < randomStartChance ? Random.Range(0f, 0.9f) : 0f;

        Vector3 pos = road.EvaluatePosition(startT);
        Vector3 fwd = road.EvaluateTangent(startT);
        fwd.y = 0f;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        pos += right * Random.Range(-0.4f, 0.4f) * halfRoadWidth + Vector3.up * spawnHeight;
        Quaternion rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(pos, rot);

        touchingWall = false;
        UpdateRoadState();
        lastT = bestT = curT;
        lastProgressTime = Time.time;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        UpdateRoadState();

        // speed in the car's own frame (forward / sideways)
        Vector3 v = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(v.z / maxSpeed);                                          // 1
        sensor.AddObservation(v.x / maxSpeed);                                          // 1

        // how far the car is turned away from the road direction
        sensor.AddObservation(Vector3.SignedAngle(transform.forward, tangent, Vector3.up) / 180f); // 1

        // which side of the centre line, and how far
        sensor.AddObservation(Mathf.Clamp(lateral, -2f, 2f));                           // 1

        // where the road goes next, in the car's own frame (lets it see corners coming)
        AddLookAhead(sensor, lookAheadNear);                                            // 2
        AddLookAhead(sensor, lookAheadFar);                                             // 2
        // Total = 8  -> Behavior Parameters "Space Size" must be 8
        // (the Ray Perception Sensor adds its own observations automatically)
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float steer = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float throttle = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f); // negative = brake

        // --- movement (simple arcade car) ---
        float speed = Mathf.Max(0f, Vector3.Dot(rb.linearVelocity, transform.forward));
        speed = Mathf.Clamp(speed + throttle * acceleration * Time.fixedDeltaTime, 0f, maxSpeed);

        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, steer * turnSpeed * Time.fixedDeltaTime, 0f));
        Vector3 vel = transform.forward * speed;
        vel.y = rb.linearVelocity.y;
        rb.linearVelocity = vel;
        rb.angularVelocity = Vector3.zero;

        // --- rewards ---
        // 1) progress along the spline, worth more when close to the centre line
        float dT = Mathf.Clamp(curT - lastT, -0.1f, 0.1f);
        lastT = curT;
        if (dT > 0f)
        {
            float centred = 1f - Mathf.Clamp01(Mathf.Abs(lateral));
            AddReward(dT * progressReward * Mathf.Lerp(0.5f, 1f, centred));
        }
        else
        {
            AddReward(dT * progressReward); // going backwards is punished
        }

        // 2) scraping along a wall (flag is set by OnCollisionStay)
        if (touchingWall)
        {
            AddReward(-wallStayPenalty);
            touchingWall = false;
        }

        // 3) small time penalty so it learns to hurry
        if (MaxStep > 0) AddReward(-1f / MaxStep);

        // 4) stuck check: no new best progress for a while -> end the episode
        if (curT > bestT + 0.001f)
        {
            bestT = curT;
            lastProgressTime = Time.time;
        }
        else if (Time.time - lastProgressTime > stuckSeconds)
        {
            AddReward(-1f);
            SetColour(loseColour);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var kb = Keyboard.current;
        if (kb == null) return;

        continuous[0] = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f); // steer
        continuous[1] = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f); // throttle
    }

    // ---------- helpers ----------

    private void UpdateRoadState()
    {
        // nearest point on the spline (the query works in the container's local space)
        Vector3 local = road.transform.InverseTransformPoint(transform.position);
        SplineUtility.GetNearestPoint(road.Spline, (float3)local, out float3 nearestLocal, out float t, 16, 3);
        curT = t;

        Vector3 nearest = road.transform.TransformPoint((Vector3)nearestLocal);

        tangent = road.EvaluateTangent(t);
        tangent.y = 0f;
        tangent.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, tangent);
        Vector3 offset = transform.position - nearest;
        offset.y = 0f;
        lateral = Vector3.Dot(offset, right) / halfRoadWidth;
    }

    private void AddLookAhead(VectorSensor sensor, float metres)
    {
        float t = Mathf.Clamp01(curT + metres / trackLength);
        Vector3 p = transform.InverseTransformPoint(road.EvaluatePosition(t));
        sensor.AddObservation(p.x / metres);
        sensor.AddObservation(p.z / metres);
    }

    private void SetColour(Color c)
    {
        if (agentMesh != null) agentMesh.material.color = c;
    }

    // ---------- collisions ----------

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Target"))
        {
            AddReward(finishBonus); // AddReward, not SetReward, so the progress reward isn't wiped
            SetColour(winColour);
            EndEpisode();
        }
    }

    private void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.CompareTag("Wall"))
        {
            AddReward(-wallHitPenalty);
            touchingWall = true;
        }
    }

    private void OnCollisionStay(Collision other)
    {
        if (other.gameObject.CompareTag("Wall")) touchingWall = true;
    }
}