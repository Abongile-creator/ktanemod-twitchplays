﻿using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class BombBinderCommander : ICommandResponder
{
    #region Constructors
    static BombBinderCommander()
    {
        _floatingHoldableType = ReflectionHelper.FindType("FloatingHoldable");
        if (_floatingHoldableType == null)
        {
            return;
        }
        _pickupTimeField = _floatingHoldableType.GetField("PickupTime", BindingFlags.Public | BindingFlags.Instance);
        _holdStateProperty = _floatingHoldableType.GetProperty("HoldState", BindingFlags.Public | BindingFlags.Instance);

        _selectableType = ReflectionHelper.FindType("Selectable");
        _handleSelectMethod = _selectableType.GetMethod("HandleSelect", BindingFlags.Public | BindingFlags.Instance);
        _handleDeselectMethod = _selectableType.GetMethod("HandleDeselect", BindingFlags.Public | BindingFlags.Instance);
        _onInteractEndedMethod = _selectableType.GetMethod("OnInteractEnded", BindingFlags.Public | BindingFlags.Instance);
        _getCurrentChildMethod = _selectableType.GetMethod("GetCurrentChild", BindingFlags.Public | BindingFlags.Instance);
        _childrenField = _selectableType.GetField("Children", BindingFlags.Public | BindingFlags.Instance);

        _selectableManagerType = ReflectionHelper.FindType("SelectableManager");
        if (_selectableManagerType == null)
        {
            return;
        }
        _selectMethod = _selectableManagerType.GetMethod("Select", BindingFlags.Public | BindingFlags.Instance);
        _handleInteractMethod = _selectableManagerType.GetMethod("HandleInteract", BindingFlags.Public | BindingFlags.Instance);
        _handleCancelMethod = _selectableManagerType.GetMethod("HandleCancel", BindingFlags.Public | BindingFlags.Instance);
        _setZSpinMethod = _selectableManagerType.GetMethod("SetZSpin", BindingFlags.Public | BindingFlags.Instance);
        _setControlsRotationMethod = _selectableManagerType.GetMethod("SetControlsRotation", BindingFlags.Public | BindingFlags.Instance);
        _getBaseHeldObjectTransformMethod = _selectableManagerType.GetMethod("GetBaseHeldObjectTransform", BindingFlags.Public | BindingFlags.Instance);
        _handleFaceSelectionMethod = _selectableManagerType.GetMethod("HandleFaceSelection", BindingFlags.Public | BindingFlags.Instance);

        _inputManagerType = ReflectionHelper.FindType("KTInputManager");
        if (_inputManagerType == null)
        {
            return;
        }
        _instanceProperty = _inputManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        _selectableManagerProperty = _inputManagerType.GetProperty("SelectableManager", BindingFlags.Public | BindingFlags.Instance);

        _inputManager = (MonoBehaviour)_instanceProperty.GetValue(null, null);
    }

    public BombBinderCommander(MonoBehaviour bombBinder)
    {
        BombBinder = bombBinder;
        Selectable = (MonoBehaviour)BombBinder.GetComponent(_selectableType);
        FloatingHoldable = (MonoBehaviour)BombBinder.GetComponent(_floatingHoldableType);
        SelectableManager = (MonoBehaviour)_selectableManagerProperty.GetValue(_inputManager, null);
    }
    #endregion

    #region Interface Implementation
    public IEnumerator RespondToCommand(string userNickName, string message, ICommandResponseNotifier responseNotifier)
    {
        if (message.Equals("hold", StringComparison.InvariantCultureIgnoreCase) ||
            message.Equals("pick up", StringComparison.InvariantCultureIgnoreCase))
        {
            IEnumerator holdCoroutine = HoldBombBinder();
            while (holdCoroutine.MoveNext())
            {
                yield return holdCoroutine.Current;
            }
        }        
        else if (message.Equals("drop", StringComparison.InvariantCultureIgnoreCase) ||
                 message.Equals("let go", StringComparison.InvariantCultureIgnoreCase) ||
                 message.Equals("put down", StringComparison.InvariantCultureIgnoreCase))
        {
            LetGoBombBinder();
        }
        else if (message.Equals("down", StringComparison.InvariantCultureIgnoreCase))
        {
            MoveDownOnPage();
        }
        else if (message.Equals("up", StringComparison.InvariantCultureIgnoreCase))
        {
            MoveUpOnPage();
        }
        else if (message.Equals("select", StringComparison.InvariantCultureIgnoreCase))
        {
            IEnumerator selectCoroutine = SelectOnPage();
            while (selectCoroutine.MoveNext())
            {
                yield return selectCoroutine.Current;
            }
        }
    }
    #endregion

    #region Helper Methods
    public IEnumerator HoldBombBinder()
    {
        int holdState = (int)_holdStateProperty.GetValue(FloatingHoldable, null);

        if (holdState != 0)
        {
            SelectObject(Selectable);

            float holdTime = (float)_pickupTimeField.GetValue(FloatingHoldable);
            IEnumerator forceRotationCoroutine = ForceHeldRotation(holdTime);
            while (forceRotationCoroutine.MoveNext())
            {
                yield return forceRotationCoroutine.Current;
            }

            yield return null;

            InitialisePage();            
        }
    }

    public void LetGoBombBinder()
    {
        int holdState = (int)_holdStateProperty.GetValue(FloatingHoldable, null);
        if (holdState == 0)
        {
            DeselectObject(Selectable);
        }
    }
    
    private void InitialisePage()
    {
        MonoBehaviour currentPage = (MonoBehaviour)Selectable.GetComponentsInChildren(_selectableType, false).Where((x) => x != Selectable).FirstOrDefault();
        _currentSelectable = (MonoBehaviour)_getCurrentChildMethod.Invoke(currentPage, null);

        _handleSelectMethod.Invoke(_currentSelectable, new object[] { true });

        _currentSelectables = (Array)_childrenField.GetValue(currentPage);

        _currentSelectableIndex = 0;
        for (; _currentSelectableIndex < _currentSelectables.Length; ++_currentSelectableIndex)
        {
            object selectable = _currentSelectables.GetValue(_currentSelectableIndex);
            if (selectable != null && _currentSelectable == (MonoBehaviour)selectable)
            {
                return;
            }
        }

        _currentSelectableIndex = int.MinValue;
    }

    private void MoveDownOnPage()
    {
        if (_currentSelectableIndex == int.MinValue || _currentSelectables == null || _currentSelectable == null)
        {
            return;
        }

        int oldSelectableIndex = _currentSelectableIndex;

        for (++_currentSelectableIndex; _currentSelectableIndex < _currentSelectables.Length; ++_currentSelectableIndex)
        {
            MonoBehaviour newSelectable = (MonoBehaviour)_currentSelectables.GetValue(_currentSelectableIndex);
            if (newSelectable != null)
            {
                _handleDeselectMethod.Invoke(_currentSelectable, new object[] { null });
                _currentSelectable = newSelectable;
                _handleSelectMethod.Invoke(_currentSelectable, new object[] { true });
                return;
            }
        }

        _currentSelectableIndex = oldSelectableIndex;
    }

    private void MoveUpOnPage()
    {
        if (_currentSelectableIndex == int.MinValue || _currentSelectables == null || _currentSelectable == null)
        {
            return;
        }

        int oldSelectableIndex = _currentSelectableIndex;

        for (--_currentSelectableIndex; _currentSelectableIndex >= 0; --_currentSelectableIndex)
        {
            MonoBehaviour newSelectable = (MonoBehaviour)_currentSelectables.GetValue(_currentSelectableIndex);
            if (newSelectable != null)
            {
                _handleDeselectMethod.Invoke(_currentSelectable, new object[] { null });
                _currentSelectable = newSelectable;
                _handleSelectMethod.Invoke(_currentSelectable, new object[] { true });
                return;
            }
        }

        _currentSelectableIndex = oldSelectableIndex;
    }

    private IEnumerator SelectOnPage()
    {
        if (_currentSelectable != null)
        {
            SelectObject(_currentSelectable);
            yield return null;
            InitialisePage();
        }
    }

    private void SelectObject(MonoBehaviour selectable)
    {
        _handleSelectMethod.Invoke(selectable, new object[] { true });
        _selectMethod.Invoke(SelectableManager, new object[] { selectable, true });
        _handleInteractMethod.Invoke(SelectableManager, null);
        _onInteractEndedMethod.Invoke(selectable, null);
    }

    private void DeselectObject(MonoBehaviour selectable)
    {
        _handleCancelMethod.Invoke(SelectableManager, null);
    }

    private IEnumerator ForceHeldRotation(float duration)
    {
        Transform baseTransform = (Transform)_getBaseHeldObjectTransformMethod.Invoke(SelectableManager, null);

        float initialTime = Time.time;
        while (Time.time - initialTime < duration)
        {
            float lerp = (Time.time - initialTime) / duration;

            Quaternion currentRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);

            _setZSpinMethod.Invoke(SelectableManager, new object[] { 0.0f });
            _setControlsRotationMethod.Invoke(SelectableManager, new object[] { baseTransform.rotation * currentRotation });
            _handleFaceSelectionMethod.Invoke(SelectableManager, null);
            yield return null;
        }

        _setZSpinMethod.Invoke(SelectableManager, new object[] { 0.0f });
        _setControlsRotationMethod.Invoke(SelectableManager, new object[] { baseTransform.rotation * Quaternion.Euler(0.0f, 0.0f, 0.0f) });
        _handleFaceSelectionMethod.Invoke(SelectableManager, null);
    }
    #endregion

    #region Readonly Fields
    public readonly MonoBehaviour BombBinder = null;
    public readonly MonoBehaviour Selectable = null;
    public readonly MonoBehaviour FloatingHoldable = null;
    private readonly MonoBehaviour SelectableManager = null;
    private readonly CoroutineCanceller CoroutineCanceller = null;
    #endregion

    #region Private Static Fields
    private static Type _floatingHoldableType = null;
    private static FieldInfo _pickupTimeField = null;
    private static PropertyInfo _holdStateProperty = null;

    private static Type _selectableType = null;
    private static MethodInfo _handleSelectMethod = null;
    private static MethodInfo _handleDeselectMethod = null;
    private static MethodInfo _onInteractEndedMethod = null;
    private static MethodInfo _getCurrentChildMethod = null;
    private static FieldInfo _childrenField = null;

    private static Type _selectableManagerType = null;
    private static MethodInfo _selectMethod = null;
    private static MethodInfo _handleInteractMethod = null;
    private static MethodInfo _handleCancelMethod = null;
    private static MethodInfo _setZSpinMethod = null;
    private static MethodInfo _setControlsRotationMethod = null;
    private static MethodInfo _getBaseHeldObjectTransformMethod = null;
    private static MethodInfo _handleFaceSelectionMethod = null;

    private static Type _inputManagerType = null;
    private static PropertyInfo _instanceProperty = null;
    private static PropertyInfo _selectableManagerProperty = null;

    private static MonoBehaviour _inputManager = null;
    #endregion

    #region Private Fields
    private MonoBehaviour _currentSelectable = null;
    private int _currentSelectableIndex = int.MinValue;
    private Array _currentSelectables = null;
    #endregion
}

