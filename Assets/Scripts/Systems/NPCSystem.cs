using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class NPCSystem : MonoBehaviour
{
    [System.Serializable]
    public class DialogueLine
    {
        [TextArea(2, 5)]
        public string text;

        [Header("Typing Settings")]
        public float letterDelay = 0.04f;
        public float lineDelay = 0.5f;

        [Header("Optional Condition")]
        public bool useMissionCondition;
        public string missionId;
        public bool showIfCompleted = true;
    }

    [Header("Dialogue Data")]
    public List<DialogueLine> dialogueLines = new List<DialogueLine>();

    [Header("Exit Dialogue")]
    [TextArea(2, 4)]
    public string exitDialogueText = "Goodbye.";
    public float exitLetterDelay = 0.04f;
    public float exitLineDelay = 0.5f;

    [Header("Interaction")]
    public float interactionRadius = 3f;
    public KeyCode interactKey = KeyCode.E;

    [Header("UI")]
    public GameObject dialoguePanel;
    public Text dialogueText;

    private int currentIndex;
    private bool dialogueActive;
    private bool isTyping;
    private Coroutine typingRoutine;
    private Transform player;
    private bool dialogueStarted = false;

    // ----------------- SIMPLE MISSION STORAGE (OPTIONAL) -----------------
    private static HashSet<string> completedMissions = new HashSet<string>();

    public static void CompleteMission(string missionId)
    {
        completedMissions.Add(missionId);
    }

    private bool IsMissionCompleted(string missionId)
    {
        return completedMissions.Contains(missionId);
    }
    // ---------------------------------------------------------------------

    void Start()
    {
        player = GameObject.Find("Player")?.transform;
        dialoguePanel.SetActive(false);
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= interactionRadius)
        {
            if (!dialogueActive && Input.GetKeyDown(interactKey))
            {
                StartDialogue();
                dialogueStarted = true;
            }
            else if (dialogueActive && Input.GetKeyDown(interactKey))
            {
                ShowNextLine();
            }
        }
        else if (dialogueStarted)
        {
            EndDialogue();
            dialogueStarted = false;
        }
    }

    // ----------------- DIALOGUE FLOW -----------------

    void StartDialogue()
    {
        dialogueActive = true;
        currentIndex = 0;
        dialoguePanel.SetActive(true);
        ShowNextLine();
    }

    void ShowNextLine()
    {
        if (isTyping)
        {
            SkipTyping();
            return;
        }

        while (currentIndex < dialogueLines.Count)
        {
            DialogueLine line = dialogueLines[currentIndex];
            currentIndex++;

            if (ShouldShowLine(line))
            {
                typingRoutine = StartCoroutine(TypeLine(line));
                return;
            }
        }

        EndDialogue();
        dialogueStarted = false;
    }

    IEnumerator TypeLine(DialogueLine line)
    {
        isTyping = true;
        dialogueText.text = "";

        foreach (char c in line.text)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(line.letterDelay);
        }

        yield return new WaitForSeconds(line.lineDelay);
        isTyping = false;
    }

    void SkipTyping()
    {
        if (typingRoutine != null)
            StopCoroutine(typingRoutine);

        dialogueText.text = dialogueLines[currentIndex - 1].text;
        isTyping = false;
    }

    void EndDialogue()
    {
        if (typingRoutine != null)
            StopCoroutine(typingRoutine);

        dialogueActive = false;
        isTyping = false;

        if (!string.IsNullOrEmpty(exitDialogueText))
        {
            StartCoroutine(ShowExitText());
        }
        else
        {
            dialoguePanel.SetActive(false);
        }
    }

    IEnumerator ShowExitText()
    {
        StartCoroutine(TypeLine(new DialogueLine()
        {
            text = exitDialogueText,
            letterDelay = exitLetterDelay,
            lineDelay = exitLineDelay
        }));
        yield return new WaitForSeconds(1.5f);
        dialoguePanel.SetActive(false);
    }

    // ----------------- CONDITIONS -----------------

    bool ShouldShowLine(DialogueLine line)
    {
        if (!line.useMissionCondition)
            return true;

        bool completed = IsMissionCompleted(line.missionId);
        return completed == line.showIfCompleted;
    }

    // ----------------- DEBUG -----------------
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}
