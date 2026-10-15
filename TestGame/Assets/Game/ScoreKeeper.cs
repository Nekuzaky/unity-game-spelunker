using System.Collections.Generic;
using UnityEngine;

namespace TestGame
{
    /// <summary>Counts the spinners in the scene and keeps a high score. Sample content for extraction tests.</summary>
    public class ScoreKeeper : MonoBehaviour
    {
        [SerializeField] private int startingScore = 10;
        [SerializeField] private List<Spinner> spinners = new List<Spinner>();

        public int Score { get; private set; }

        private void Awake()
        {
            Score = startingScore + spinners.Count * 5;
            Debug.Log($"TestGame ready with {spinners.Count} spinners, score {Score}, best {PlayerPrefs.GetInt("best", 0)}");
        }

        public void AddPoints(int points)
        {
            Score += points;
            if (Score > PlayerPrefs.GetInt("best", 0))
                PlayerPrefs.SetInt("best", Score);
        }
    }
}
