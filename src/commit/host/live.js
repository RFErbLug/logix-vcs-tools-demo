(() => {
  const api = window.LogixVcsVisual;
  if (!api) return;

  const commitButton = document.getElementById('commitBtn');
  const resetButton = document.getElementById('resetBtn');
  const refreshStatusButton = document.getElementById('refreshStatusBtn');
  const savedProjectStatus = document.getElementById('savedProjectStatus');
  const pushButton = document.getElementById('pushBtn');
  const pullButton = document.getElementById('pullBtn');
  const pushStatus = document.getElementById('pushStatus');
  const commitStatus = document.getElementById('commitStatus');
  const messageDisplay = document.getElementById('message');
  const versionDisplay = document.getElementById('version');
  const hashDisplay = document.getElementById('hash');
  const versionCard = document.getElementById('versionCard');
  const remoteState = document.getElementById('remoteState');
  const packet = document.getElementById('packet');
  const actionRow = commitButton?.closest('.actions');
  const setupMount = document.getElementById('setupMount');
  const branchDisplay = document.getElementById('branch');
  const projectDisplay = document.getElementById('projectMeta');
  const repoDisplay = document.getElementById('repoMeta');
  const contextState = document.getElementById('contextState');
  const cliCommand = document.getElementById('cliCommand');
  const cliContext = document.getElementById('cliContext');
  const resultHero = document.getElementById('resultHero');
  const resultTitle = document.getElementById('resultTitle');
  const resultBody = document.getElementById('resultBody');
  const resultChangeCount = document.getElementById('resultChangeCount');
  const shareState = document.getElementById('shareState');
  const publishSection = document.getElementById('publishSection');
  const localPublishState = document.getElementById('localPublishState');
  const openRepoButton = document.getElementById('openRepoBtn');
  const openRemoteButton = document.getElementById('openRemoteBtn');

  let live = false;
  let messageInput = null;
  let currentMeta = null;
  let setupShell = null;
  let errorLines = [];
  let captureInFlight = false;
  let restoreRequired = false;
  let lastObservedProjectWrite = null;
  let lastVerifiedProjectWrite = null;

  const setState = text => {
    if (contextState) contextState.textContent = text;
  };

  const setResultMode = mode => {
    if (!resultHero) return;
    resultHero.classList.remove('ready', 'success', 'neutral', 'fail');
    resultHero.classList.add(mode);
  };

  const renderSharingState = meta => {
    const hasRemote = Boolean(meta?.remoteConfigured);
    const hasVersion = Boolean(meta?.latestSha || meta?.version);
    const ahead = Number(meta?.ahead || 0);
    const behind = Number(meta?.behind || 0);

    if (publishSection) publishSection.hidden = false;
    if (openRepoButton) openRepoButton.disabled = !meta?.configPresent;
    if (openRemoteButton) openRemoteButton.disabled = !hasRemote;

    if (pushButton) pushButton.disabled = true;
    if (pullButton) {
      pullButton.disabled = true;
      pullButton.textContent = 'Pull';
    }

    if (localPublishState) {
      localPublishState.textContent = hasVersion
        ? `${meta?.version || 'Current version'} - ${meta?.latestSha || 'local Git'}`
        : (meta?.configPresent ? 'Repository ready - no captured project version yet' : 'Project not connected yet');
    }

    if (!meta?.configPresent) {
      if (shareState) shareState.textContent = 'Not connected';
      if (remoteState) remoteState.textContent = 'Not configured';
      if (pushStatus) pushStatus.textContent = 'Connect the project before synchronizing.';
      return;
    }

    if (!hasRemote) {
      if (shareState) shareState.textContent = 'Local only - no remote configured';
      if (remoteState) remoteState.textContent = 'Not configured';
      if (pushStatus) pushStatus.textContent = 'No remote repository configured.';
      return;
    }

    if (remoteState) {
      remoteState.textContent = meta.originUrl
        ? `origin - ${meta.originUrl}`
        : 'Shared Git remote configured.';
    }

    if (pullButton) pullButton.disabled = false;

    if (ahead > 0 && behind > 0) {
      if (shareState) shareState.textContent = `Diverged - local +${ahead}, remote +${behind}`;
      if (pushStatus) pushStatus.textContent = 'Local and remote both have newer commits. Use a full Git client to reconcile them.';
      if (pushButton) pushButton.disabled = true;
      if (pullButton) {
        pullButton.disabled = true;
        pullButton.textContent = 'Diverged';
      }
      return;
    }

    if (behind > 0) {
      if (shareState) shareState.textContent = `Remote has ${behind} newer commit${behind === 1 ? '' : 's'}`;
      if (pushStatus) pushStatus.textContent = 'Remote commits are available. Pull to fast-forward the local repository.';
      if (pushButton) pushButton.disabled = true;
      if (pullButton) {
        pullButton.disabled = false;
        pullButton.textContent = 'Pull';
      }
      return;
    }

    if (!hasVersion) {
      if (shareState) shareState.textContent = 'Remote configured - no captured project version yet';
      if (pushStatus) pushStatus.textContent = 'Pull from the remote or capture the first project version.';
      if (pushButton) pushButton.disabled = true;
      if (pullButton) pullButton.textContent = 'Pull';
      return;
    }

    if (meta.published) {
      if (shareState) shareState.textContent = 'Local and remote are up to date';
      if (pushStatus) pushStatus.textContent = 'No unpublished local commits are detected.';
      if (pushButton) pushButton.disabled = true;
      if (pullButton) pullButton.textContent = 'Pull';
      return;
    }

    if (ahead > 0) {
      if (shareState) shareState.textContent = `Local repository ahead by ${ahead} commit${ahead === 1 ? '' : 's'}`;
      if (pushStatus) pushStatus.textContent = 'Ready to publish the local repository state.';
      if (pushButton) pushButton.disabled = false;
      if (pullButton) pullButton.textContent = 'Pull';
      return;
    }

    if (shareState) shareState.textContent = 'Local version - ready to publish';
    if (pushStatus) pushStatus.textContent = 'Ready to publish this exact local version.';
    if (pushButton) pushButton.disabled = false;
    if (pullButton) pullButton.textContent = 'Pull';
  };

  const renderResultFromMeta = meta => {
    if (!resultTitle || !resultBody) return;

    if (!meta?.configPresent) {
      setResultMode('neutral');
      resultTitle.textContent = 'Setup required';
      resultBody.textContent = 'Connect this ACD to a Git repository before capturing the first version.';
      if (resultChangeCount) resultChangeCount.textContent = 'Not checked yet';
      return;
    }

    const projectTime = meta?.projectLastWriteIso ? Date.parse(meta.projectLastWriteIso) : NaN;
    const commitTime = meta?.latestCommitIso ? Date.parse(meta.latestCommitIso) : NaN;
    const newerSave = Number.isFinite(projectTime) &&
      Number.isFinite(commitTime) &&
      projectTime > commitTime + 1000 &&
      meta?.projectLastWriteIso !== lastVerifiedProjectWrite;

    if (newerSave) {
      setResultMode('ready');
      resultTitle.textContent = 'Ready to capture';
      resultBody.textContent = 'A newer ACD save was detected. Capture it to compare the saved engineering content with the current Git version.';
      if (resultChangeCount) resultChangeCount.textContent = 'Waiting for comparison';
      return;
    }

    if (meta?.version && meta?.latestSha) {
      setResultMode('success');
      resultTitle.textContent = `${meta.version} is current`;
      resultBody.textContent = meta.latestMessage || 'The saved ACD matches the latest captured Git version.';
      if (resultChangeCount) {
        const files = Number(meta.latestChangedFiles || 0);
        resultChangeCount.textContent = files > 0
          ? `${files} source file${files === 1 ? '' : 's'} in this version`
          : 'Captured version';
      }
      return;
    }

    setResultMode('ready');
    resultTitle.textContent = 'Ready for the first version';
    resultBody.textContent = 'Save the ACD in Studio 5000, describe the engineering change, then capture it.';
    if (resultChangeCount) resultChangeCount.textContent = 'Not checked yet';
  };

  const setLiveIdleState = meta => {
    if (versionCard && !versionCard.classList.contains('live')) {
      if (versionDisplay) versionDisplay.textContent = meta?.version || '—';
      if (hashDisplay) hashDisplay.textContent = meta?.latestSha || '—';
      if (messageDisplay) {
        messageDisplay.textContent = meta?.latestMessage
          ? `${meta.latestMessage} - latest captured Git version`
          : (meta?.version ? `Latest local history is ${meta.version}.` : 'No local version captured yet');
      }
    }

    if (packet) packet.textContent = meta?.version || '—';
    renderSharingState(meta);

    if (savedProjectStatus) {
      const projectTime = meta?.projectLastWriteIso ? Date.parse(meta.projectLastWriteIso) : NaN;
      const commitTime = meta?.latestCommitIso ? Date.parse(meta.latestCommitIso) : NaN;
      if (Number.isFinite(projectTime) &&
          Number.isFinite(commitTime) &&
          projectTime > commitTime + 1000 &&
          meta?.projectLastWriteIso !== lastVerifiedProjectWrite) {
        savedProjectStatus.innerHTML = '<b>Newer save detected.</b> The ACD was saved after the latest capture. Capture it to check whether the engineering content actually changed.';
        setState('Newer save detected');
      } else if (meta?.latestSha) {
        savedProjectStatus.textContent = 'Saved ACD matches the latest captured version. Unsaved Studio 5000 edits are never included.';
      } else {
        savedProjectStatus.textContent = 'Save the ACD in Studio 5000 before the first capture.';
      }
    }
  };

  const meaningfulFailure = fallback => {
    const candidate = errorLines
      .map(line => line.replace(/^ERROR:\s*/i, '').trim())
      .find(line => line &&
        !line.startsWith('at ') &&
        !line.startsWith('---') &&
        !line.includes('System.CommandLine.Invocation.InvocationPipeline.InvokeAsync'));
    return candidate || fallback || 'Commit failed. See the live CLI output for details.';
  };

  const renderTroubleshooting = meta => {
    if (!meta) return;

    const projectPath = meta.projectPath || '(project path unavailable)';
    const configPath = meta.configPath || '(config not created yet)';
    const repoPath = meta.repoRoot || meta.repoPath || '(repository not connected yet)';
    const branch = meta.branch || '(no commit/branch resolved yet)';
    const tool = meta.l5xGitPath || 'l5xgit.exe';
    const command = `${tool} commit --acd "${projectPath}"`;

    if (cliCommand) cliCommand.textContent = command;
    if (cliContext) {
      cliContext.textContent = [
        `Project:    ${projectPath}`,
        `Config:     ${configPath}`,
        `Repository: ${repoPath}`,
        `Branch:     ${branch}`,
        '',
        'Equivalent underlying command:',
        command,
        '',
        'The visual host wraps this command; l5xgit remains authoritative for the commit operation.'
      ].join('\n');
    }
  };

  const removeMessageField = () => {
    const field = document.getElementById('liveCommitMessageShell');
    if (field) field.remove();
    messageInput = null;
  };

  const configureMessageField = enabled => {
    removeMessageField();
    if (!enabled || !actionRow) return;

    const shell = document.createElement('label');
    shell.id = 'liveCommitMessageShell';
    shell.className = 'message-entry';

    const title = document.createElement('strong');
    title.textContent = 'Describe this change';

    const helper = document.createElement('span');
    helper.textContent = 'This becomes the Git commit message for the captured project version.';
    helper.style.color = 'var(--muted)';
    helper.style.fontSize = '14px';

    messageInput = document.createElement('input');
    messageInput.id = 'liveCommitMessage';
    messageInput.type = 'text';
    messageInput.value = '';
    messageInput.placeholder = currentMeta?.projectName
      ? `Describe the ${currentMeta.projectName} engineering change`
      : 'Describe the engineering change';
    messageInput.autocomplete = 'off';

    shell.append(title, helper, messageInput);
    actionRow.parentNode.insertBefore(shell, actionRow);
  };

  const removeSetup = () => {
    if (setupShell) setupShell.remove();
    setupShell = null;
  };

  const post = async (url, body) => {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body)
    });

    if (!response.ok) {
      let detail = `${response.status} ${response.statusText}`;
      try {
        const payload = await response.json();
        detail = payload.error || detail;
      } catch {}
      throw new Error(detail);
    }

    const type = response.headers.get('content-type') || '';
    return type.includes('application/json') ? response.json() : null;
  };

  const showSetup = meta => {
    removeSetup();
    removeMessageField();
    if (!setupMount) return;

    const shell = document.createElement('section');
    shell.id = 'liveProjectSetup';
    shell.className = 'setup-box';

    const kicker = document.createElement('span');
    kicker.textContent = 'FIRST-TIME SETUP';
    kicker.style.color = 'var(--ra-red)';
    kicker.style.fontSize = '12px';
    kicker.style.fontWeight = '900';
    kicker.style.letterSpacing = '.12em';

    const title = document.createElement('strong');
    title.textContent = 'Connect this project to Version Control';

    const helper = document.createElement('span');
    helper.textContent = `One-time setup for ${meta.projectFileName || 'this ACD'}. Choose the exact folder where this project's exploded Logix source should live. That folder may be inside a larger Git repository. The tool detects the repository root separately and writes ${meta.projectName || '<Project>'}_L5xGit.yml beside the ACD.`;
    helper.style.color = 'var(--muted)';
    helper.style.fontSize = '16px';
    helper.style.lineHeight = '1.55';

    const pathLabel = document.createElement('label');
    pathLabel.style.display = 'grid';
    pathLabel.style.gap = '6px';

    const pathTitle = document.createElement('span');
    pathTitle.textContent = 'Logix source destination folder';
    pathTitle.style.fontWeight = '800';

    const pathInput = document.createElement('input');
    pathInput.type = 'text';
    const suggestedRepo = (() => {
      const dir = meta.projectDirectory || '';
      const name = meta.projectName || 'project';
      if (!dir) return `C:\\Projects\\${name}-vcs`;
      return `${dir}\\${name}-vcs`;
    })();
    pathInput.value = suggestedRepo;
    pathInput.autocomplete = 'off';

    const pathRow = document.createElement('div');
    pathRow.style.display = 'grid';
    pathRow.style.gridTemplateColumns = '1fr auto';
    pathRow.style.gap = '10px';

    const browseButton = document.createElement('button');
    browseButton.type = 'button';
    browseButton.className = 'btn ghost';
    browseButton.textContent = 'Browse…';
    browseButton.title = 'Choose the Logix source destination folder in Windows';

    browseButton.addEventListener('click', async () => {
      browseButton.disabled = true;
      setupStatus.textContent = 'Opening Windows folder picker…';
      try {
        const picked = await post('/api/pick-folder', { initialPath: pathInput.value.trim() });
        if (picked?.path) {
          pathInput.value = picked.path;
          setupStatus.textContent = 'Logix source folder selected.';
          pathInput.focus();
        } else {
          setupStatus.textContent = 'Folder selection canceled.';
        }
      } catch (error) {
        setupStatus.textContent = error.message;
      } finally {
        browseButton.disabled = false;
      }
    });

    pathRow.append(pathInput, browseButton);
    pathLabel.append(pathTitle, pathRow);

    const promptLabel = document.createElement('label');
    promptLabel.style.display = 'flex';
    promptLabel.style.gap = '9px';
    promptLabel.style.alignItems = 'center';
    const promptInput = document.createElement('input');
    promptInput.type = 'checkbox';
    promptInput.checked = true;
    promptLabel.append(promptInput, document.createTextNode('Ask for a commit message each time'));

    const initLabel = document.createElement('label');
    initLabel.style.display = 'flex';
    initLabel.style.gap = '9px';
    initLabel.style.alignItems = 'center';
    const initInput = document.createElement('input');
    initInput.type = 'checkbox';
    initInput.checked = true;
    initLabel.append(initInput, document.createTextNode('If no containing Git repository exists, create a dedicated repository here'));

    const setupButton = document.createElement('button');
    setupButton.type = 'button';
    setupButton.className = 'btn';
    setupButton.textContent = 'Connect project';
    setupButton.style.justifySelf = 'start';

    const setupStatus = document.createElement('div');
    setupStatus.style.color = 'var(--muted)';
    setupStatus.style.fontSize = '12px';
    setupStatus.setAttribute('aria-live', 'polite');

    pathInput.addEventListener('keydown', event => {
      if (event.key === 'Enter') {
        event.preventDefault();
        setupButton.click();
      }
    });

    setupButton.addEventListener('click', async () => {
      const destinationPath = pathInput.value.trim();
      if (!destinationPath) {
        setupStatus.textContent = 'Choose the Logix source destination folder first.';
        pathInput.focus();
        return;
      }

      setupButton.disabled = true;
      setupStatus.textContent = 'Connecting project…';
      try {
        const updatedMeta = await post('/api/setup', {
          repositoryPath: destinationPath,
          promptForCommitMessage: promptInput.checked,
          initializeRepoIfMissing: initInput.checked
        });
        setupStatus.textContent = 'Project connected. Git repository and Logix source destination are ready.';
        applyMeta(updatedMeta);
      } catch (error) {
        setupStatus.textContent = error.message;
        setupButton.disabled = false;
      }
    });

    shell.append(kicker, title, helper, pathLabel, promptLabel, initLabel, setupButton, setupStatus);
    setupMount.appendChild(shell);
    setupMount.scrollIntoView({ behavior: 'smooth', block: 'start' });
    setupShell = shell;
  };

  const applyMeta = meta => {
    if (!meta) return;
    currentMeta = meta;

    if (projectDisplay) projectDisplay.textContent = meta.projectFileName || meta.projectName || 'Current ACD';
    if (branchDisplay) branchDisplay.textContent = meta.branch || (meta.configPresent ? 'No commits yet' : 'Not connected');
    if (repoDisplay) repoDisplay.textContent = meta.repoRoot || meta.repoPath || 'Not connected';

    if (meta.configPresent) {
      removeSetup();
      configureMessageField(Boolean(meta.promptForCommitMessage));
      commitButton.disabled = restoreRequired || !meta.l5xgitConfigured;
      commitStatus.textContent = restoreRequired
        ? 'Repository source was updated from remote. The ACD was not overwritten. Use Tools -> Restore Working Project before capturing again.'
        : (meta.l5xgitConfigured
          ? (meta.version ? 'Project connected. Save any new Studio 5000 changes, then capture another version.' : 'Project connected. Save the ACD, then capture the first version.')
          : 'Project is configured, but l5xgit.exe is not available.');
      setState(restoreRequired ? 'Restore required' : (meta.l5xgitConfigured ? 'Ready' : 'Tool unavailable'));
    } else {
      commitButton.disabled = true;
      pushButton.disabled = true;
      commitStatus.textContent = 'New project detected. Complete the one-time repository setup below.';
      setState('Setup required');
      showSetup(meta);
    }

    api.setMeta({ branch: meta.branch || undefined, version: meta.version || undefined });
    setLiveIdleState(meta);
    renderResultFromMeta(meta);
    renderTroubleshooting(meta);
  };

  const applyRawStage = line => {
    if (!line || /^ERROR:/i.test(line)) return;

    if (/Copying ACD to temp path/i.test(line)) {
      api.setStage('copy','active');
      setState('Copying project');
    } else if (/Open project started|Open project succeeded|Save As started/i.test(line)) {
      api.setStage('copy','done');
      api.setStage('export','active');
      setState('Exporting L5X');
    } else if (/Save As succeeded|Logix Project was closed/i.test(line)) {
      api.setStage('copy','done');
      api.setStage('export','done');
    } else if (/l5xploding L5X/i.test(line)) {
      api.setStage('copy','done');
      api.setStage('export','done');
      api.setStage('explode','active');
      setState('Exploding project');
    } else if (/Committing to Git repository/i.test(line)) {
      api.setStage('copy','done');
      api.setStage('export','done');
      api.setStage('explode','done');
      api.setStage('stage','active');
      setState('Staging changes');
    } else if (/Changes committed successfully with commit ID:/i.test(line)) {
      api.setStage('stage','done');
      api.setStage('commit','active');
      setState('Creating commit');
    }
  };

  const refreshMetaQuietly = async () => {
    if (!live || captureInFlight) return;
    try {
      const response = await fetch('/api/meta', { cache: 'no-store' });
      if (!response.ok) return;
      const meta = await response.json();
      const previousWrite = lastObservedProjectWrite;
      lastObservedProjectWrite = meta.projectLastWriteIso || null;
      currentMeta = meta;

      if (projectDisplay) projectDisplay.textContent = meta.projectFileName || meta.projectName || 'Current ACD';
      if (branchDisplay) branchDisplay.textContent = meta.branch || (meta.configPresent ? 'No commits yet' : 'Not connected');
      if (repoDisplay) repoDisplay.textContent = meta.repoRoot || meta.repoPath || 'Not connected';
      api.setMeta({ branch: meta.branch || undefined, version: meta.version || undefined });
      setLiveIdleState(meta);

      if (previousWrite && lastObservedProjectWrite && previousWrite !== lastObservedProjectWrite) {
        lastVerifiedProjectWrite = null;
        renderResultFromMeta(meta);
        commitStatus.innerHTML = '<b>Studio 5000 save detected.</b> Capture another version to compare the saved engineering content with Git.';
        commitButton.textContent = meta.version ? 'Capture another version' : 'Capture version';
      }
    } catch {
      // Quiet polling should never interrupt the demo.
    }
  };

  const connect = async () => {
    try {
      const response = await fetch('/api/meta', { cache: 'no-store' });
      if (!response.ok) return;
      const meta = await response.json();
      if (!meta.live) return;

      live = true;
      lastObservedProjectWrite = meta.projectLastWriteIso || null;
      commitButton.textContent = meta.version ? 'Capture another version' : 'Capture version';
      pushButton.textContent = 'Publish to remote';
      applyMeta(meta);

      const stream = new EventSource('/events');
      stream.onmessage = event => {
        const data = JSON.parse(event.data);
        switch (data.type) {
          case 'reset':
            errorLines = [];
            api.reset();
            commitButton.textContent = 'Capture version';
            pushButton.textContent = 'Publish to remote';
            setLiveIdleState(currentMeta);
            setState('Starting capture');
            break;
          case 'configured':
            applyMeta(data.meta);
            break;
          case 'meta':
            api.setMeta(data);
            if (data.branch && branchDisplay) branchDisplay.textContent = data.branch;
            break;
          case 'raw':
            if (data.line) {
              api.log(data.line);
              applyRawStage(data.line);
              if (/^ERROR:/i.test(data.line)) errorLines.push(data.line);
            }
            break;
          case 'stage':
            // Live stage state is derived from raw l5xgit output above. This avoids
            // coupling the presentation to older message patterns in the host.
            break;
          case 'commit_success':
            captureInFlight = false;
            errorLines = [];
            currentMeta = {
              ...currentMeta,
              version: data.version || currentMeta?.version,
              branch: data.branch || currentMeta?.branch,
              latestSha: data.sha || currentMeta?.latestSha,
              latestMessage: data.message || currentMeta?.latestMessage,
              latestChangedFiles: Number(data.changedFiles || 0),
              published: false,
              ahead: Number(currentMeta?.ahead || 0) + 1
            };
            api.commitSuccess(data);
            setResultMode('success');
            if (resultTitle) resultTitle.textContent = `${data.version || 'New version'} created`;
            if (resultBody) resultBody.textContent = data.message || 'Engineering version captured successfully.';
            if (resultChangeCount) {
              const files = Number(data.changedFiles || 0);
              resultChangeCount.textContent = files > 0
                ? `${files} source file${files === 1 ? '' : 's'} changed`
                : 'Engineering source captured';
            }
            renderSharingState(currentMeta);
            commitButton.textContent = 'Capture another version';
            commitStatus.innerHTML = '<b>Version created.</b> Make the next engineering change in Studio 5000, save the ACD, then capture again.';
            if (savedProjectStatus) savedProjectStatus.textContent = 'Captured. Make the next change in Studio 5000 and SAVE before capturing again.';
            setState(`${data.version || 'Version'} created`);
            if (data.branch && branchDisplay) branchDisplay.textContent = data.branch;
            break;
          case 'commit_no_changes':
            captureInFlight = false;
            errorLines = [];
            api.setStage('stage', 'done');
            api.setStage('commit', 'done');

            const compareMark = document.querySelector('[data-stage="stage"] .mark');
            if (compareMark) compareMark.textContent = 'Matches current version';

            const commitMark = document.querySelector('[data-stage="commit"] .mark');
            if (commitMark) commitMark.textContent = 'Not needed';

            lastVerifiedProjectWrite = currentMeta?.projectLastWriteIso || lastObservedProjectWrite;
            setResultMode('neutral');
            if (resultTitle) resultTitle.textContent = 'Already up to date';
            if (resultBody) resultBody.textContent = 'No engineering changes were found. The saved ACD already matches the current Git version. No new version was needed.';
            if (resultChangeCount) resultChangeCount.textContent = 'No engineering changes';
            renderSharingState(currentMeta);
            commitStatus.innerHTML = '<b>Already up to date.</b> The saved ACD matches the current Git version. No new version was needed.';
            if (savedProjectStatus) savedProjectStatus.textContent = 'Saved ACD matches the current Git version.';
            commitButton.disabled = false;
            commitButton.textContent = currentMeta?.version ? 'Capture another version' : 'Capture version';
            setState('Already up to date');
            break;
          case 'commit_failure':
            captureInFlight = false;
            const failureMessage = meaningfulFailure(data.message);
            api.commitFailure(failureMessage);
            setLiveIdleState(currentMeta);
            setResultMode('fail');
            if (resultTitle) resultTitle.textContent = 'Needs attention';
            if (resultBody) resultBody.textContent = failureMessage;
            if (resultChangeCount) resultChangeCount.textContent = 'Capture did not complete';
            commitButton.disabled = false;
            setState('Capture stopped');
            break;
          case 'pull_start':
            if (pullButton) pullButton.disabled = true;
            if (pushButton) pushButton.disabled = true;
            if (shareState) shareState.textContent = data.apply ? 'Updating from remoteâ€¦' : 'Checking remoteâ€¦';
            if (pushStatus) pushStatus.textContent = data.apply
              ? 'Fetching the remote, then applying a fast-forward-only update.'
              : 'Refreshing remote-tracking information. No working files will be changed.';
            setState(data.apply ? 'Updating from remote' : 'Checking remote');
            break;
          case 'pull_available':
            currentMeta = {
              ...currentMeta,
              ...(data.meta || {}),
              ahead: Number(data.ahead || 0),
              behind: Number(data.behind || 0),
              published: false
            };
            renderSharingState(currentMeta);
            if (pushStatus) pushStatus.textContent = data.message || 'Remote updates are available.';
            setState('Remote update available');
            break;
          case 'pull_no_changes':
            currentMeta = {
              ...currentMeta,
              ...(data.meta || {}),
              ahead: Number(data.ahead || 0),
              behind: Number(data.behind || 0)
            };
            renderSharingState(currentMeta);
            if (pushStatus) pushStatus.textContent = data.message || 'No newer remote commits were found.';
            setState('Remote checked');
            break;
          case 'pull_success':
            restoreRequired = true;
            currentMeta = {
              ...currentMeta,
              ...(data.meta || {}),
              ahead: 0,
              behind: 0,
              published: true
            };
            renderSharingState(currentMeta);
            commitButton.disabled = true;
            setResultMode('ready');
            if (resultTitle) resultTitle.textContent = 'Repository updated';
            if (resultBody) {
              const count = Number(data.updatedCommits || 0);
              resultBody.textContent =
                `${count || 'Remote'} newer commit${count === 1 ? '' : 's'} were applied with fast-forward-only Git. ` +
                'The saved/open ACD was not overwritten. Use Tools -> Restore Working Project before capturing again.';
            }
            if (resultChangeCount) resultChangeCount.textContent = 'ACD unchanged';
            if (savedProjectStatus) {
              savedProjectStatus.textContent =
                'Remote source is newer. The ACD was intentionally left unchanged; restore the desired Git version before the next capture.';
            }
            commitStatus.innerHTML =
              '<b>Repository updated.</b> The ACD was not overwritten. Use Tools -> Restore Working Project before capturing again.';
            if (pushStatus) pushStatus.textContent = data.message || 'Repository updated from remote.';
            setState('Restore required');
            break;
          case 'pull_failure':
            if (data.meta) currentMeta = { ...currentMeta, ...data.meta };
            renderSharingState(currentMeta);
            if (pushStatus) pushStatus.textContent = data.message || 'Remote update stopped.';
            setState('Remote update stopped');
            break;          case 'push_start':
            api.pushStart(data);
            if (shareState) shareState.textContent = 'Publishing…';
            setState('Publishing');
            break;
          case 'push_success':
            api.pushSuccess(data);
            currentMeta = { ...currentMeta, published: true, ahead: 0 };
            renderSharingState(currentMeta);
            setState('Published');
            break;
          case 'push_failure':
            api.pushFailure(data.message || 'Push failed.');
            renderSharingState(currentMeta);
            setState('Publish failed');
            break;
        }
      };
      stream.onerror = () => {
        api.log('Live event connection interrupted. The browser will retry automatically.');
      };
    } catch {
      // Opening index.html directly remains the standalone prototype mode.
    }
  };

  commitButton.addEventListener('click', async event => {
    if (!live) return;
    event.preventDefault();
    event.stopImmediatePropagation();

    if (!currentMeta?.configPresent) {
      commitStatus.textContent = 'Connect this project to version control first.';
      return;
    }

    errorLines = [];
    captureInFlight = true;
    commitButton.disabled = true;
    setResultMode('ready');
    if (resultTitle) resultTitle.textContent = 'Capturing…';
    if (resultBody) resultBody.textContent = 'Reading the saved ACD and comparing its engineering content with Git.';
    if (resultChangeCount) resultChangeCount.textContent = 'Comparing…';
    setState('Starting capture');
    try {
      const message = messageInput?.value.trim() || '';
      await post('/api/commit', { message });
    } catch (error) {
      captureInFlight = false;
      api.commitFailure(meaningfulFailure(error.message));
      setLiveIdleState(currentMeta);
      commitButton.disabled = false;
      setState('Capture stopped');
    }
  }, true);

  openRepoButton?.addEventListener('click', async event => {
    if (!live) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    openRepoButton.disabled = true;
    try {
      await post('/api/open-repo');
    } catch (error) {
      commitStatus.textContent = `Could not open repository folder: ${error.message}`;
    } finally {
      openRepoButton.disabled = !currentMeta?.configPresent;
    }
  }, true);

  openRemoteButton?.addEventListener('click', async event => {
    if (!live) return;
    event.preventDefault();
    event.stopImmediatePropagation();

    openRemoteButton.disabled = true;

    try {
      await post('/api/open-remote');
    } catch (error) {
      if (pushStatus) pushStatus.textContent = `Could not open remote repository: ${error.message}`;
    } finally {
      openRemoteButton.disabled = !currentMeta?.remoteConfigured;
    }
  }, true);
  refreshStatusButton?.addEventListener('click', async event => {
    if (!live) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    refreshStatusButton.disabled = true;
    setState('Refreshing status');
    try {
      const response = await fetch('/api/meta', { cache: 'no-store' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const meta = await response.json();
      applyMeta(meta);
      commitButton.textContent = meta.version ? 'Capture another version' : 'Capture version';
      setState('Ready');
    } catch (error) {
      commitStatus.textContent = `Status refresh failed: ${error.message}`;
      setState('Refresh failed');
    } finally {
      refreshStatusButton.disabled = false;
    }
  }, true);

  resetButton?.addEventListener('click', event => {
    if (!live) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    api.reset();
    setLiveIdleState(currentMeta);
    setState(currentMeta?.configPresent ? 'Ready' : 'Setup required');
  }, true);

  pullButton?.addEventListener('click', async event => {
    if (!live) return;
    event.preventDefault();
    event.stopImmediatePropagation();

    if (!currentMeta?.configPresent || !currentMeta?.remoteConfigured) {
      if (pushStatus) pushStatus.textContent = 'Connect a remote repository before pulling.';
      return;
    }

    pullButton.disabled = true;
    pushButton.disabled = true;
    setState('Pulling');

    try {
      await post('/api/pull', { apply: true });
    } catch (error) {
      renderSharingState(currentMeta);
      if (pushStatus) pushStatus.textContent = `Pull failed: ${error.message}`;
      setState('Pull stopped');
    }
  }, true);
  pushButton.addEventListener('click', async event => {
    if (!live) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    pushButton.disabled = true;
    setState('Publishing');
    try {
      await post('/api/push');
    } catch (error) {
      api.pushFailure(error.message);
      setState('Publish failed');
    }
  }, true);

  connect().then(() => {
    // Keep the long-lived page aware of saves made back in Studio 5000.
    window.setInterval(refreshMetaQuietly, 3000);
  });
})();
