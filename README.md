# OUR ABSENCE - Unity Project

## Project Overview

This repository contains the Unity project **OUR ABSENCE**. Follow the instructions below to set up the project on a new local PC and manage it using GitHub Desktop.

 ## Game concept:
A relentless one-way journey through an endless, lifeless, and shrouded world—a descent into darkness in search of a distant hope.
This is the dawn of a new age, the birth of a world drowned in shadow. From the void emerges faint fragments of light—fragile sparks of creation. But with creation comes war, with war comes death, and with death, the light begins to wither.
You are cast into this decaying land, where every step forward is a struggle against despair. The path cannot be turned back; the only way is through the darkness. To endure, you must uncover what little light remains before it is forever consumed.

---

## Project Structure

```
Assets/
│── Animations/        
│── Audio/             
│── Effects/          
│── Materials/         
│── Meshes/            
│── Objects/           
│── Prefabs/          
│── Resources/        
│── Scenes/           
│── Scripts/           
│── Settings/          
│── Textures/          
│── UI/                
│── World/             
│── TutorialInfo/     
```

---

## Clone the Project to a New PC

To start working on this Unity project on another computer, follow these steps:

### **1. Install Required Software**

Make sure you have the following installed:

- [Unity Hub](https://unity.com/) and the correct **Unity version** used for this project.
- Optional [GitHub Desktop](https://desktop.github.com/) for version control.

### **2. Clone the Repository**

1. Open **GitHub Desktop**.
2. Click **"File" > "Clone repository"**.
3. Select **"GitHub.com"** and find the repository named **OUR ABSENCE**.
4. Choose a local folder where you want to store the project (e.g., `C:/UnityProjects/OurAbsence/`).
5. Click **"Clone"** and wait for the process to complete.
   Alternatively just open terminal on the project dir and use git commands through it (make sure git is in your terminal pip line)

### **3. Open the Project in Unity**

1. Open **Unity Hub**.
2. Click **"Open"** and navigate to the folder where you cloned the project.
3. Select the **unity files OUR ABSENCE** project folder in **our absence** and click **"Open"**.
4. Unity will load the project. Make sure the correct Unity version is selected.

---

## 🛠 Working with Git & GitHub Desktop

### **1. Committing Changes**

Whenever you make changes, follow these steps to commit them:

1. Open **GitHub Desktop**.
2. You will see the list of modified files in the repository.
3. Write a short commit message describing your changes (e.g., "Updated player movement script").
4. Click **"Commit to"**.

### **2. Pushing Changes to GitHub**

After committing changes:

1. Click **"Push origin"** to upload your changes to GitHub.
2. Other team members can now pull the latest version.

### **3. Pulling the Latest Changes**

Before making new changes, always pull the latest updates:

1. Open **GitHub Desktop**.
2. Click **"Fetch origin"** to check for new changes.
3. Click **"Pull origin"** to update your local copy.

### **4. Resolving Merge Conflicts**

If GitHub Desktop shows a **merge conflict**, you will need to manually resolve it:

1. Open the conflicting file in **Unity or a text editor**.
2. Review the changes and decide which version to keep.
3. After resolving, **commit** and **push** the changes.

### **5. Git Flow Of Work**

1. In your gitHub Desktop pull dev origin for updates in dev branch
2. Create a new branch from dev name it significantly for example "improve/fix-movment" (⚠SPECIAL CHARACTERS LIKE ` ~ ( ) | ect ARE NOT ALLWED, BRANCH WITH THIS CHARACTERS WILL BE DELETED)
3. Work as you like on this branch commit relevent progresses
4. Make a push request to dev origin you can do that from the Desktop GitHub and or from the terminal or Web
5. Wait for review if all good the branch and your changes will be pushed to dev and than to main by the owner.

---

## ⚠ Important Notes

- **DO NOT** commit the `Library/`, `Logs/`, or `Temp/` folders. They sould be automatically ignored in `.gitignore`.
- Always **pull the latest changes** before making edits.
- Use **GitHub Desktop** for managing commits and branches, Or read the git commands documentation. 

---
