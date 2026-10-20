import { StyleSheet, TextInput, type TextInputProps, View } from 'react-native';
import { Text } from './text';
import { useColors } from './theme';
import { fonts, minTouchTarget, radius, space } from './tokens';

type TextFieldProps = Pick<
  TextInputProps,
  | 'value'
  | 'onChangeText'
  | 'onBlur'
  | 'onSubmitEditing'
  | 'keyboardType'
  | 'placeholder'
  | 'autoCapitalize'
  | 'maxLength'
> & {
  readonly label: string;
  readonly hint?: string;
  readonly error?: string;
  readonly isMonospace?: boolean;
};

export function TextField({ label, hint, error, isMonospace = false, ...input }: TextFieldProps) {
  const colors = useColors();
  const hasError = error !== undefined;
  return (
    <View style={styles.field}>
      <Text variant="label" tone="textMuted">
        {label.toUpperCase()}
      </Text>
      <TextInput
        accessibilityLabel={label}
        accessibilityHint={error ?? hint}
        autoCorrect={false}
        placeholderTextColor={colors.textMuted}
        returnKeyType="done"
        style={[
          styles.input,
          {
            color: colors.text,
            backgroundColor: colors.surface,
            borderColor: hasError ? colors.danger : colors.border,
            fontFamily: isMonospace ? fonts.mono : fonts.bodyMedium,
          },
        ]}
        {...input}
      />
      {hasError ? (
        <Text variant="caption" tone="danger" accessibilityRole="alert" accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : (
        hint && (
          <Text variant="caption" tone="textMuted">
            {hint}
          </Text>
        )
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  field: { gap: space.xs },
  input: {
    minHeight: minTouchTarget + 6,
    borderWidth: 1.5,
    borderRadius: radius.md,
    paddingHorizontal: space.lg,
    fontSize: 17,
  },
});
