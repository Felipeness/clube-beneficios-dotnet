import { useForm, useStore } from '@tanstack/react-form';
import { Link } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { z } from 'zod';
import { describeError } from '@/api/error-messages';
import { MembershipId } from '@/api/schemas';
import { Button } from '@/ui/button';
import { visibleError } from '@/ui/form-errors';
import { Notice } from '@/ui/notice';
import { Text } from '@/ui/text';
import { TextField } from '@/ui/text-field';
import { space } from '@/ui/tokens';
import { useMemberSignIn, useOperatorSignIn } from './sign-in';

const memberEntry = z.object({
  membershipId: z.string().trim().pipe(z.guid('Cole o código inteiro da adesão (8-4-4-4-12 caracteres).')),
});

export function MemberEntryForm() {
  const signIn = useMemberSignIn();
  const form = useForm({
    defaultValues: { membershipId: '' },
    validators: { onChange: memberEntry },
    onSubmit: ({ value }) => signIn.mutate(MembershipId.parse(value.membershipId.trim())),
  });
  const submissionAttempts = useStore(form.store, (state) => state.submissionAttempts);

  return (
    <View style={styles.form}>
      <form.Field name="membershipId">
        {(field) => (
          <TextField
            label="Código da adesão"
            placeholder="0199f0a4-7c1e-7000-8000-…"
            hint="Aparece logo depois da adesão. Guarde para entrar de novo."
            value={field.state.value}
            onChangeText={field.handleChange}
            onBlur={field.handleBlur}
            onSubmitEditing={() => void form.handleSubmit()}
            error={visibleError(field.state.meta, submissionAttempts)}
            autoCapitalize="none"
            isMonospace
          />
        )}
      </form.Field>
      {signIn.isError && <Notice tone="error" {...describeError(signIn.error)} />}
      <Button
        label="Entrar como membro"
        icon="arrow-right"
        onPress={() => void form.handleSubmit()}
        isLoading={signIn.isPending}
      />
      <View style={styles.joinRow}>
        <Text tone="textMuted">Ainda não é membro?</Text>
        <Link href="/join" accessibilityRole="link" style={styles.link}>
          <Text variant="bodyStrong" tone="primary">
            Aderir agora
          </Text>
        </Link>
      </View>
    </View>
  );
}

const operatorEntry = z.object({
  operatorName: z
    .string()
    .trim()
    .min(2, 'Digite pelo menos 2 letras.')
    .max(40, 'Use até 40 caracteres.')
    .regex(/^[\p{L}\p{N} ._-]+$/u, 'Use letras, números, espaço, ponto, hífen ou sublinhado.'),
});

export function OperatorEntryForm() {
  const signIn = useOperatorSignIn();
  const form = useForm({
    defaultValues: { operatorName: '' },
    validators: { onChange: operatorEntry },
    onSubmit: ({ value }) => signIn.mutate(value.operatorName.trim()),
  });
  const submissionAttempts = useStore(form.store, (state) => state.submissionAttempts);

  return (
    <View style={styles.form}>
      <form.Field name="operatorName">
        {(field) => (
          <TextField
            label="Seu nome"
            placeholder="ana"
            hint="Fica registrado em cada estorno que você fizer."
            value={field.state.value}
            onChangeText={field.handleChange}
            onBlur={field.handleBlur}
            onSubmitEditing={() => void form.handleSubmit()}
            error={visibleError(field.state.meta, submissionAttempts)}
            autoCapitalize="none"
            maxLength={40}
          />
        )}
      </form.Field>
      {signIn.isError && <Notice tone="error" {...describeError(signIn.error)} />}
      <Button
        label="Abrir o balcão"
        icon="store-outline"
        onPress={() => void form.handleSubmit()}
        isLoading={signIn.isPending}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  form: { gap: space.md },
  joinRow: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: space.xs, justifyContent: 'center' },
  link: { paddingVertical: space.sm },
});
