using UnityEngine;

namespace TestGame
{
    /// <summary>Rotates its object and bobs it up and down. Sample content for extraction tests.</summary>
    public class Spinner : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 90f;
        [SerializeField] private float bobHeight = 0.25f;
        [SerializeField] private string label = "Spinner";

        private Vector3 _start;

        public string Label => label;

        private void Start()
        {
            _start = transform.position;
        }

        private void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f);
            transform.position = _start + Vector3.up * Mathf.Sin(Time.time * 2f) * bobHeight;
        }
    }
}
