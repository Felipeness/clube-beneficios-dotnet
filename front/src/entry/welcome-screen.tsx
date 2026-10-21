import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import { useState } from 'react';
import { Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import Animated, { FadeInDown, FadeInUp } from 'react-native-reanimated';
import type { IconName } from '@/ui/button';
import { Card } from '@/ui/card';
import { Screen } from '@/ui/screen';
import { Text } from '@/ui/text';
import { useColors } from '@/ui/theme';
import { Ticket } from '@/ui/ticket';
import { space } from '@/ui/tokens';
import { MemberEntryForm, OperatorEntryForm } from './entry-forms';

type Audience = 'member' | 'operator';

type Choice = {
  readonly audience: Audience;
  readonly title: string;
  readonly detail: string;
  readonly icon: IconName;
  readonly stub: string;
};

const choices: readonly Choice[] = [
  {
    audience: 'member',
    title: 'Sou membro',
    detail: 'Ver saldo, pagar o Pix do mês e trocar crédito por recompensa.',
    icon: 'ticket-percent-outline',
    stub: 'R$ 20 / mês',
  },
  {
    audience: 'operator',
    title: 'Sou operador',
    detail: 'Consultar membros, estornar lançamentos e acompanhar a saúde do livro-caixa.',
    icon: 'store-outline',
    stub: 'Balcão',
  },
];

export function WelcomeScreen() {
  const [audience, setAudience] = useState<Audience | null>(null);
  const isWide = useWindowDimensions().width >= 640;

  return (
    <Screen>
      <Hero />
      <View style={[styles.choices, isWide && styles.choicesWide]} accessibilityRole="radiogroup">
        {choices.map((choice, index) => (
          <Animated.View
            key={choice.audience}
            entering={FadeInDown.delay(120 + index * 90).duration(380)}
            style={isWide && styles.choiceWide}
          >
            <ChoiceTicket
              choice={choice}
              isSelected={audience === choice.audience}
              onPress={() => setAudience(choice.audience)}
            />
          </Animated.View>
        ))}
      </View>
      {audience && (
        <Animated.View key={audience} entering={FadeInUp.duration(260)}>
          <Card>{audience === 'member' ? <MemberEntryForm /> : <OperatorEntryForm />}</Card>
        </Animated.View>
      )}
    </Screen>
  );
}

function Hero() {
  return (
    <Animated.View entering={FadeInDown.duration(420)} style={styles.hero}>
      <Text variant="label" tone="accent">
        CLUBE DE BENEFÍCIOS
      </Text>
      <Text variant="hero" accessibilityRole="header">
        Seu mês vira crédito.
      </Text>
      <Text variant="hero" tone="primary">
        Seu crédito vira mimo.
      </Text>
      <Text tone="textMuted" style={styles.lede}>
        Cada Pix de R$ 20,00 entra como crédito no seu saldo. Troque por ingresso de cinema, kit de perfume ou garrafa
        térmica, sem pegadinha: cada centavo aparece no extrato.
      </Text>
    </Animated.View>
  );
}

type ChoiceTicketProps = { readonly choice: Choice; readonly isSelected: boolean; readonly onPress: () => void };

function ChoiceTicket({ choice, isSelected, onPress }: ChoiceTicketProps) {
  const colors = useColors();
  const isMember = choice.audience === 'member';
  const ink = isMember ? 'onCard' : 'text';
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ checked: isSelected }}
      accessibilityLabel={`${choice.title}. ${choice.detail}`}
      onPress={onPress}
      style={({ pressed }) => ({
        opacity: pressed ? 0.85 : 1,
        transform: [{ rotate: isMember ? '-0.6deg' : '0.6deg' }],
      })}
    >
      <Ticket
        tone={isMember ? 'green' : 'paper'}
        isHighlighted={isSelected}
        stub={
          <>
            <MaterialCommunityIcons name={choice.icon} size={28} color={isMember ? colors.accent : colors.primary} />
            <Text variant="caption" tone={isMember ? 'onCardMuted' : 'textMuted'} style={styles.stubText}>
              {choice.stub}
            </Text>
          </>
        }
      >
        <Text variant="heading" tone={ink}>
          {choice.title}
        </Text>
        <Text tone={isMember ? 'onCardMuted' : 'textMuted'}>{choice.detail}</Text>
      </Ticket>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  hero: { paddingTop: space.xxxl, paddingBottom: space.lg, gap: space.xs },
  lede: { marginTop: space.md, maxWidth: 560 },
  choices: { gap: space.lg },
  choicesWide: { flexDirection: 'row' },
  choiceWide: { flex: 1 },
  stubText: { textAlign: 'center' },
});
