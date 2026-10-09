using Unity.MLAgents;

using Unity.MLAgents.Actuators;

using Unity.MLAgents.Sensors;

using UnityEngine;

using UnityEngine.InputSystem;

public class MoveToTargetAgent : Agent

{

    [SerializeField] private Transform target;

    [SerializeField] private float moveSpeed = 3f;

    [SerializeField] private Renderer floor;

    [SerializeField] private Color winColour = Color.green;

    [SerializeField] private Color loseColour = Color.red;

    [SerializeField] private Color idleColour = Color.grey;

    public override void OnEpisodeBegin()

    {

        transform.localPosition = new Vector3(

        Random.Range(-4f, 4f), 0.5f, Random.Range(-4f, 0f));

        target.localPosition = new Vector3(-3.18f, 0.5f, 3.2f);

        //Random.Range(-4f, 4f), 0.5f, Random.Range(1f, 4f));

    }
    public override void CollectObservations(VectorSensor sensor)

    {

        sensor.AddObservation(transform.localPosition); // 3

        sensor.AddObservation(target.localPosition); // 3

    }
    public override void OnActionReceived(ActionBuffers actions)

    {

        float moveX = actions.ContinuousActions[0];

        float moveZ = actions.ContinuousActions[1];

        Vector3 move = new Vector3(moveX, 0f, moveZ);

        transform.localPosition += move * moveSpeed * Time.deltaTime;

        // Small penalty every step so it learns to hurry

        if (MaxStep > 0) AddReward(-1f / MaxStep);

    }
    public override void Heuristic(in ActionBuffers actionsOut)

    {

        var continuous = actionsOut.ContinuousActions;

        var kb = Keyboard.current;

        if (kb == null) return;

        continuous[0] = (kb.dKey.isPressed ? 1f : 0f)

        - (kb.aKey.isPressed ? 1f : 0f);

        continuous[1] = (kb.wKey.isPressed ? 1f : 0f)

        - (kb.sKey.isPressed ? 1f : 0f);

    }
    public override void Initialize() // runs once at startup

    {

        floor.material.color = idleColour;

    }

    private void OnTriggerEnter(Collider other)

    {

        if (other.CompareTag("Target"))

        {

            SetReward(1f);

            floor.material.color = winColour;

            EndEpisode();

        }

        else if (other.CompareTag("Wall"))

        {

            SetReward(-1f);

            floor.material.color = loseColour;

            EndEpisode();

        }

    }
}